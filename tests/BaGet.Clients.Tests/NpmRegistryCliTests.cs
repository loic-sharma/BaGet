using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Clients.Tests;

/// <summary>
/// The real <c>npm</c> CLI (10.x, in <c>node:22-alpine</c>) against the real BaGet application on a real socket: publish,
/// install, view, search, dist-tag, scoped packages, deprecate, unpublish, login, whoami, logout, private repositories and a
/// large tarball. These are the end-to-end proof of the registry protocol; the tests skip without a Docker daemon.
/// </summary>
public class NpmRegistryCliTests
{
    internal const string Repository = "web";
    internal const string GlobalKey = "global-key-for-cli";

    // npm writes its configuration to $HOME/.npmrc; the other settings keep the output to what the tests assert on.
    internal static string Npmrc(BaGetServer server, string? token, string repository = Repository, string extra = "")
    {
        var lines = $"registry={server.RegistryUrl(repository)}\\nupdate-notifier=false\\nfund=false\\naudit=false\\nprogress=false\\n{extra}";
        if (token != null) lines += $"{server.AuthKey(repository)}={token}\\n";

        return $"printf '{lines}' > $HOME/.npmrc\n";
    }

    internal static string Package(string dir, string name, string version, string extraJson = "", string? readme = null)
        => $$"""
            mkdir -p {{dir}} && cd {{dir}}
            cat > package.json <<'JSON'
            {"name":"{{name}}","version":"{{version}}","description":"A test package","main":"index.js","license":"MIT"{{extraJson}}}
            JSON
            echo 'module.exports = function (s) { return "{{name}}@{{version}}:" + s; };' > index.js
            printf '{{readme ?? "# " + name + " readme"}}\n' > README.md

            """.Replace("\r\n", "\n");

    // `npm login` reads the user name and the password from a terminal, which a test has none of, so this runs what npm itself
    // calls for it (npm-profile's login: the web login first, the CouchDB-style user call when the registry answers 404) and
    // stores the token the way npm login does.
    private static string Login(BaGetServer server, string user, string password, string repository = Repository)
        => $$"""
            cat > login.js <<'JS'
            const profile = require('/usr/local/lib/node_modules/npm/node_modules/npm-profile');
            (async () => {
              const opts = { registry: '{{server.RegistryUrl(repository)}}' };
              const result = await profile.login(
                async () => { throw new Error('no browser'); },
                async () => ({ username: '{{user}}', password: '{{password}}', email: '{{user}}@example.org' }),
                opts);
              console.log(result.token);
            })().catch(e => { console.error('login failed', e.code, e.message); process.exit(1); });
            JS
            token=$(node login.js) || exit 1
            npm config set "{{server.AuthKey(repository)}}" "$token"

            """;

    private static string Clean(string script) => script.Replace("\r\n", "\n");

    internal static async Task<(BaGetServer Server, DotNet.Testcontainers.Containers.IContainer Node)> StartAsync(IDictionary<string, string>? settings = null, bool anonymousRead = true)
    {
        var node = await ClientContainers.NodeAsync();
        var server = await BaGetServer.StartAsync(settings);
        await server.CreateNpmRepositoryAsync(Repository, anonymousRead);

        return (server, node);
    }

    internal static async Task<CliResult> RunAsync(DotNet.Testcontainers.Containers.IContainer node, string script)
    {
        return await ClientContainers.RunAsync(node, Clean(script));
    }

    [Fact]
    public async Task APackagePublishedWithNpmInstallsWithNpmAndTheRegistryHoldsWhatWasPublished()
    {
        var (server, node) = await StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await using var _ = server;

        var publish = await RunAsync(node, Npmrc(server, GlobalKey) + Package("pub", "left-pad", "1.0.0") + "npm publish\n");
        Assert.True(publish.ExitCode == 0, publish.All);
        Assert.Contains("+ left-pad@1.0.0", publish.Stdout);

        var package = await server.PackageAsync(Repository, "left-pad");
        Assert.NotNull(package);
        Assert.Equal("1.0.0", package!.DistTags["latest"]);
        Assert.StartsWith("sha512-", package.Versions.Single().Integrity);
        Assert.Contains("left-pad readme", package.Versions.Single().Readme);

        // A different client directory with no token: the repository is open to readers.
        var consume = await RunAsync(node, Npmrc(server, null) + """
            mkdir app && cd app
            echo '{"name":"app","version":"1.0.0"}' > package.json
            npm install left-pad
            node -e "console.log(require('left-pad')('x'))"
            echo "--- view"
            npm view left-pad version
            npm view left-pad dist.tarball
            echo "--- lock"
            grep -E '"(resolved|integrity)"' package-lock.json
            """);
        Assert.True(consume.ExitCode == 0, consume.All);
        Assert.Contains("left-pad@1.0.0:x", consume.Stdout);
        Assert.Contains($"http://{server.ContainerAuthority}/r/web/npm/left-pad/-/left-pad-1.0.0.tgz", consume.Stdout);
        Assert.Contains(package.Versions.Single().Integrity, consume.Stdout);
    }

    [Fact]
    public async Task AScopedPackageIsPublishedAndInstalledThroughTheEncodedSlash()
    {
        var (server, node) = await StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await using var _ = server;

        var publish = await RunAsync(node, Npmrc(server, GlobalKey) + Package("pub", "@acme/tool", "2.1.0") + "npm publish --access public\n");
        Assert.True(publish.ExitCode == 0, publish.All);
        Assert.Contains("+ @acme/tool@2.1.0", publish.Stdout);
        Assert.NotNull(await server.PackageAsync(Repository, "@acme/tool"));

        // The scope is routed to this registry; everything else would still come from the default one.
        var consume = await RunAsync(node, """
            printf '@acme:registry=REGISTRY\nupdate-notifier=false\nfund=false\naudit=false\n' > $HOME/.npmrc
            mkdir app && cd app
            echo '{"name":"app","version":"1.0.0"}' > package.json
            npm install @acme/tool
            node -e "console.log(require('@acme/tool')('y'))"
            npm view @acme/tool version
            """.Replace("REGISTRY", server.RegistryUrl(Repository)));
        Assert.True(consume.ExitCode == 0, consume.All);
        Assert.Contains("@acme/tool@2.1.0:y", consume.Stdout);
        Assert.Contains("2.1.0", consume.Stdout);
    }

    [Fact]
    public async Task DistTagsPrereleasesAndInstallByTag()
    {
        var (server, node) = await StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await using var _ = server;

        var script = Npmrc(server, GlobalKey)
            + Package("v1", "left-pad", "1.0.0") + "npm publish\ncd ..\n"
            + Package("v2", "left-pad", "2.0.0-beta.1") + "npm publish --tag next\ncd ..\n"
            + """
              echo "--- ls"
              npm dist-tag ls left-pad
              npm dist-tag add left-pad@1.0.0 stable
              echo "--- ls2"
              npm dist-tag ls left-pad
              mkdir app && cd app
              echo '{"name":"app","version":"1.0.0"}' > package.json
              npm install left-pad@next
              node -e "console.log(require('left-pad')('t'))"
              npm install left-pad@stable
              node -e "console.log(require('left-pad')('t'))"
              cd ..
              npm dist-tag rm left-pad stable
              echo "--- ls3"
              npm dist-tag ls left-pad
              """;
        var result = await RunAsync(node, script);

        Assert.True(result.ExitCode == 0, result.All);
        var sections = result.Stdout.Split("--- ", StringSplitOptions.RemoveEmptyEntries);
        var ls = sections.Single(s => s.StartsWith("ls\n"));
        Assert.Contains("latest: 1.0.0", ls);
        Assert.Contains("next: 2.0.0-beta.1", ls);
        var ls2 = sections.Single(s => s.StartsWith("ls2\n"));
        Assert.Contains("stable: 1.0.0", ls2);
        var afterInstall = ls2.Substring(ls2.IndexOf('\n') + 1);
        Assert.Contains("left-pad@2.0.0-beta.1:t", afterInstall);
        Assert.Contains("left-pad@1.0.0:t", afterInstall);
        Assert.DoesNotContain("stable", sections.Single(s => s.StartsWith("ls3\n")));
    }

    [Fact]
    public async Task DeprecatingAVersionShowsInViewAndWarnsOnInstall()
    {
        var (server, node) = await StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await using var _ = server;

        var result = await RunAsync(node, Npmrc(server, GlobalKey)
            + Package("v1", "left-pad", "1.0.0") + "npm publish\ncd ..\n"
            + Package("v2", "left-pad", "1.1.0") + "npm publish\ncd ..\n"
            + """
              npm deprecate left-pad@1.0.0 "use 1.1.0 instead"
              echo "--- view"
              npm view left-pad@1.0.0 deprecated
              npm view left-pad@1.1.0 deprecated
              echo "--- install"
              mkdir app && cd app
              echo '{"name":"app","version":"1.0.0"}' > package.json
              npm install left-pad@1.0.0 2>&1
              cd ..
              npm deprecate left-pad@1.0.0 ""
              echo "--- cleared"
              npm view left-pad@1.0.0 deprecated
              """);

        Assert.True(result.ExitCode == 0, result.All);
        var view = result.Stdout.Split("--- ")[1];
        Assert.Contains("use 1.1.0 instead", view);
        Assert.Contains("deprecated", result.Stdout.Split("--- ")[2]);
        Assert.Contains("use 1.1.0 instead", result.Stdout.Split("--- ")[2]);
        Assert.DoesNotContain("deprecated", result.Stdout.Split("--- ")[3].Replace("--- cleared", string.Empty));
        Assert.Null((await server.PackageAsync(Repository, "left-pad"))!.Versions.Single(v => v.Version == "1.0.0").Deprecated);
    }

    [Fact]
    public async Task UnpublishingAVersionAndThenThePackageWithTheNpmClient()
    {
        var (server, node) = await StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await using var _ = server;

        var result = await RunAsync(node, Npmrc(server, GlobalKey)
            + Package("v1", "left-pad", "1.0.0") + "npm publish\ncd ..\n"
            + Package("v2", "left-pad", "1.1.0") + "npm publish\ncd ..\n"
            + """
              npm unpublish left-pad@1.1.0
              echo "--- versions"
              npm view left-pad versions --json
              npm view left-pad dist-tags --json
              echo "--- tarball"
              wget -q -S -O /dev/null TARBALL 2>&1 | head -1
              echo "--- all"
              npm unpublish left-pad --force
              npm view left-pad version 2>&1
              """.Replace("TARBALL", server.RegistryUrl(Repository) + "left-pad/-/left-pad-1.1.0.tgz"));

        Assert.True(result.ExitCode == 0 || result.Stdout.Contains("--- all"), result.All);
        var sections = result.Stdout.Split("--- ", StringSplitOptions.RemoveEmptyEntries);
        var versions = sections.Single(s => s.StartsWith("versions\n"));
        Assert.Contains("1.0.0", versions);
        Assert.DoesNotContain("1.1.0", versions);
        Assert.Contains("\"latest\": \"1.0.0\"", versions);
        Assert.Contains("404", sections.Single(s => s.StartsWith("tarball\n")));
        Assert.Contains("E404", result.All);
        Assert.Null(await server.PackageAsync(Repository, "left-pad"));
    }

    [Fact]
    public async Task SearchPingWhoamiAndAuditAnswerTheClient()
    {
        var (server, node) = await StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await using var _ = server;

        var result = await RunAsync(node, Npmrc(server, GlobalKey, extra: "") + Package("pub", "left-pad", "1.0.0") + "npm publish\ncd ..\n" + """
            echo "--- search"
            npm search pad --json
            echo "--- ping"
            npm ping 2>&1
            echo "--- whoami"
            npm whoami
            echo "--- audit"
            mkdir app && cd app
            echo '{"name":"app","version":"1.0.0"}' > package.json
            printf 'audit=true\n' >> $HOME/.npmrc
            npm install left-pad 2>&1
            npm audit 2>&1
            """);

        Assert.True(result.ExitCode == 0, result.All);
        var sections = result.Stdout.Split("--- ", StringSplitOptions.RemoveEmptyEntries);
        using (var search = JsonDocument.Parse(sections.Single(s => s.StartsWith("search\n")).Substring("search\n".Length).Split("--- ")[0]))
        {
            Assert.Equal("left-pad", search.RootElement[0].GetProperty("name").GetString());
        }

        Assert.Contains("PONG", sections.Single(s => s.StartsWith("ping\n")));
        Assert.Contains("superuser", sections.Single(s => s.StartsWith("whoami\n")));
        Assert.Contains("0 vulnerabilities", sections.Single(s => s.StartsWith("audit\n")));
    }

    [Fact]
    public async Task LoginWhoamiLogoutAndTheTokenOfAPrivateRepository()
    {
        var (server, node) = await StartAsync(
            new Dictionary<string, string> { ["Accounts:Enabled"] = "true", ["ApiKey"] = GlobalKey },
            anonymousRead: false);
        await using var _ = server;
        await server.CreateUserAsync("alice");
        await server.CreateUserAsync("bob");
        await server.GrantReadAsync("alice", Repository);

        // A private repository: anyone without a token is told to authenticate (npm answers E401 and does not retry).
        var anonymous = await RunAsync(node, Npmrc(server, null) + "npm view left-pad version 2>&1; echo \"exit=$?\"\n");
        Assert.Contains("E401", anonymous.All);

        // `npm login`: web login is refused with 404, so npm asks for the user name and password and PUTs the user document.
        var login = await RunAsync(node, Npmrc(server, null) + Login(server, "alice", BaGetServer.Password) + "npm whoami\ncat $HOME/.npmrc\n");
        Assert.True(login.ExitCode == 0, login.All);
        Assert.StartsWith("alice", login.Stdout.TrimStart());
        Assert.Contains($"{server.AuthKey(Repository)}=bgt_", login.Stdout);

        var wrong = await RunAsync(node, Npmrc(server, null) + Login(server, "alice", "wrong-password"));
        Assert.NotEqual(0, wrong.ExitCode);
        Assert.Contains("401", wrong.All);

        // The token reads and publishes; alice owns what she published; a second user cannot publish a version of it.
        var aliceToken = (await server.CreateKeyAsync("alice", Repository));
        var bobToken = (await server.CreateKeyAsync("bob", Repository));
        var publish = await RunAsync(node, Npmrc(server, aliceToken) + Package("pub", "left-pad", "1.0.0") + "npm publish 2>&1\ncd ..\nnpm view left-pad version\n");
        Assert.True(publish.ExitCode == 0, publish.All);
        Assert.Contains("1.0.0", publish.Stdout);

        var foreign = await RunAsync(node, Npmrc(server, bobToken) + Package("pub", "left-pad", "1.1.0") + "npm publish 2>&1\n");
        Assert.NotEqual(0, foreign.ExitCode);
        Assert.Contains("E403", foreign.All);
        Assert.Single((await server.PackageAsync(Repository, "left-pad"))!.Versions);
    }

    [Fact]
    public async Task NpmLogoutRevokesTheTokenItLoggedInWith()
    {
        var (server, node) = await StartAsync(new Dictionary<string, string> { ["Accounts:Enabled"] = "true", ["ApiKey"] = GlobalKey });
        await using var _ = server;
        await server.CreateUserAsync("alice");
        var registry = server.RegistryUrl(Repository);

        var result = await RunAsync(node, Npmrc(server, null) + $$"""
            {{Login(server, "alice", BaGetServer.Password)}}
            echo "--- before"
            npm whoami 2>&1
            npm logout --registry={{registry}} 2>&1
            echo "--- after"
            grep -c _authToken $HOME/.npmrc
            """);

        var sections = result.Stdout.Split("--- ", StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("alice", sections.Single(s => s.StartsWith("before\n")));
        Assert.Contains("0", sections.Single(s => s.StartsWith("after\n")));

        // The token the login created is gone from the account's keys.
        using var scope = server.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<BaGet.Core.BaGetUser>>();
        var alice = (await users.FindByNameAsync("alice"))!;
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync(alice.Id, TestContext.Current.CancellationToken));
    }
}
