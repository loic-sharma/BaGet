using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Clients.Tests;

/// <summary>
/// The real <c>npm</c> CLI (B6) against a recording server on the host: what it sends for a read, a publish and a login, and how
/// it authenticates (B3). These are the facts the npm registry of the next phase is built on; they run in a
/// <c>node:22-alpine</c> container and skip without Docker.
/// </summary>
public class NpmClientFlowTests
{
    private const string Token = "s3cr3t-token";

    private const string Packument = """
        {"_id":"left-pad","name":"left-pad","dist-tags":{"latest":"1.0.0"},
         "versions":{"1.0.0":{"name":"left-pad","version":"1.0.0","dist":{"tarball":"http://example.invalid/left-pad/-/left-pad-1.0.0.tgz","shasum":"0000000000000000000000000000000000000000"}}}}
        """;

    // npm writes its configuration to $HOME/.npmrc; update-notifier=false stops its "is there a newer npm" request to the registry.
    private static string Npmrc(HostRegistry registry, bool withToken = true, string extra = "")
    {
        var lines = $"registry={registry.ContainerUrl}\\nupdate-notifier=false\\nfund=false\\naudit=false\\n{extra}";
        if (withToken) lines += $"//{registry.ContainerAuthority}/:_authToken={Token}\\n";

        return $"printf '{lines}' > $HOME/.npmrc; ";
    }

    private static HttpResponseData ServePackument(Recorded request)
        => request.Target == "/left-pad" ? new HttpResponseData(200, Packument) : new HttpResponseData(404);

    [Fact]
    public async Task TheRigRunsTheRealNpmAndTheContainerReachesTheHost()
    {
        var node = await ClientContainers.NodeAsync();
        await using var registry = await HostRegistry.StartAsync(_ => new HttpResponseData(200, "{}"));

        var version = await ClientContainers.RunAsync(node, "npm --version");
        Assert.Equal(0, version.ExitCode);
        Assert.True(int.Parse(version.Stdout.Trim().Split('.')[0]) >= 9, "npm " + version.Stdout);

        var reach = await ClientContainers.RunAsync(node, $"wget -q -O - {registry.ContainerUrl}ping");
        Assert.Equal(0, reach.ExitCode);
        Assert.Equal("/ping", Assert.Single(registry.Requests).Target);
    }

    [Fact]
    public async Task AReadSendsTheAuthTokenAsBearerAndAskForThePackumentByName()
    {
        var node = await ClientContainers.NodeAsync();
        await using var registry = await HostRegistry.StartAsync(ServePackument);

        var result = await ClientContainers.RunAsync(node, Npmrc(registry) + "npm view left-pad version");

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Equal("1.0.0", result.Stdout.Trim());
        var request = Assert.Single(registry.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/left-pad", request.Target);
        Assert.Equal("Bearer " + Token, request.Authorization);
    }

    [Fact]
    public async Task WithoutATokenNpmSendsNoCredentialsAndReportsE401WhenTheRegistryRefuses()
    {
        var node = await ClientContainers.NodeAsync();
        await using var registry = await HostRegistry.StartAsync(_ => new HttpResponseData(401, """{"error":"authentication required"}"""));

        var result = await ClientContainers.RunAsync(node, Npmrc(registry, withToken: false) + "npm view left-pad version");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("E401", result.All);
        // npm never sends credentials unprompted: the first request is without them, and it does not retry after a 401 challenge.
        Assert.All(registry.Requests, r => Assert.Null(r.Authorization));
    }

    [Fact]
    public async Task WhoamiAsksTheRegistryWhoOwnsTheToken()
    {
        var node = await ClientContainers.NodeAsync();
        await using var registry = await HostRegistry.StartAsync(r => r.Target == "/-/whoami" ? new HttpResponseData(200, """{"username":"alice"}""") : new HttpResponseData(404));

        var result = await ClientContainers.RunAsync(node, Npmrc(registry) + "npm whoami");

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Equal("alice", result.Stdout.Trim());
        var request = Assert.Single(registry.Requests);
        Assert.Equal("/-/whoami", request.Target);
        Assert.Equal("Bearer " + Token, request.Authorization);
    }

    [Fact]
    public async Task LoginFallsBackToTheCouchDbUserCallWhenTheRegistryHasNoWebLogin()
    {
        var node = await ClientContainers.NodeAsync();
        await using var registry = await HostRegistry.StartAsync(r =>
        {
            if (r.Method == "PUT" && r.Target.StartsWith("/-/user/org.couchdb.user:", StringComparison.Ordinal))
            {
                return new HttpResponseData(201, JsonSerializer.Serialize(new { ok = true, id = "org.couchdb.user:alice", rev = "1-x", token = Token }));
            }

            return r.Target == "/-/whoami" && r.Authorization == "Bearer " + Token
                ? new HttpResponseData(200, """{"username":"alice"}""")
                : new HttpResponseData(404);
        });

        // `npm login` prompts on a terminal, so a script drives the library npm itself calls for it (npm-profile's login: web login
        // first, the CouchDB-style user call when the registry answers 404). Then the token is stored the way npm login stores it.
        var script = Npmrc(registry, withToken: false) + $$"""
            cat > login.js <<'JS'
            const profile = require('/usr/local/lib/node_modules/npm/node_modules/npm-profile');
            (async () => {
              const opts = { registry: '{{registry.ContainerUrl}}' };
              const result = await profile.login(
                async () => { throw new Error('no browser'); },
                async () => ({ username: 'alice', password: 'pw', email: 'alice@example.org' }),
                opts);
              console.log(result.token);
            })().catch(e => { console.error('login failed', e.code, e.message); process.exit(1); });
            JS
            token=$(node login.js) || exit 1
            npm config set "//{{registry.ContainerAuthority}}/:_authToken" "$token"
            npm whoami
            """;
        var result = await ClientContainers.RunAsync(node, script.Replace("\r\n", "\n"));

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Equal("alice", result.Stdout.Trim());

        var requests = registry.Requests;
        var attempts = requests.Select(r => r.Method + " " + r.Target).ToList();
        Assert.Equal(new[] { "POST /-/v1/login", "PUT /-/user/org.couchdb.user:alice", "GET /-/whoami" }, attempts);
        Assert.Null(requests[0].Authorization);
        Assert.Null(requests[1].Authorization);

        using var body = JsonDocument.Parse(requests[1].Body);
        Assert.Equal("alice", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("pw", body.RootElement.GetProperty("password").GetString());
        Assert.Equal("org.couchdb.user:alice", body.RootElement.GetProperty("_id").GetString());
        Assert.Equal("user", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("Bearer " + Token, requests[2].Authorization);
    }

    [Fact]
    public async Task APublishIsOnePutOfThePackumentWithTheTarballInlineAsBase64()
    {
        var node = await ClientContainers.NodeAsync();
        await using var registry = await HostRegistry.StartAsync(r => r.Method == "PUT" ? new HttpResponseData(200, """{"ok":true}""") : new HttpResponseData(404));

        var script = Npmrc(registry)
            + """echo '{"name":"@acme/tool","version":"1.2.3","description":"d"}' > package.json; """
            + """echo 'hello' > index.js; npm publish --access public""";
        var result = await ClientContainers.RunAsync(node, script);

        Assert.True(result.ExitCode == 0, result.All);
        var request = Assert.Single(registry.Requests, r => r.Method == "PUT");

        // A scoped name travels with an encoded slash; the token is Bearer; the body is JSON.
        Assert.Equal("/@acme%2ftool", request.Target);
        Assert.Equal("Bearer " + Token, request.Authorization);
        Assert.Equal("application/json", request.Headers["content-type"]);
        Assert.Equal("publish", request.Headers["npm-command"]);

        using var document = JsonDocument.Parse(request.Body);
        var root = document.RootElement;
        Assert.Equal("@acme/tool", root.GetProperty("name").GetString());
        Assert.Equal("1.2.3", root.GetProperty("dist-tags").GetProperty("latest").GetString());
        Assert.Equal("public", root.GetProperty("access").GetString());

        var version = root.GetProperty("versions").GetProperty("1.2.3");
        Assert.Equal("@acme/tool@1.2.3", version.GetProperty("_id").GetString());
        var dist = version.GetProperty("dist");
        Assert.StartsWith("sha512-", dist.GetProperty("integrity").GetString());
        Assert.Equal(40, dist.GetProperty("shasum").GetString()!.Length);
        // The tarball URL the client writes (it repeats the scope in the file name, as does the attachment key) is a placeholder
        // the registry replaces with its own address, conventionally /@scope/name/-/name-1.2.3.tgz.
        Assert.EndsWith("/@acme/tool/-/@acme/tool-1.2.3.tgz", dist.GetProperty("tarball").GetString());

        var attachment = root.GetProperty("_attachments").GetProperty("@acme/tool-1.2.3.tgz");
        Assert.Equal("application/octet-stream", attachment.GetProperty("content_type").GetString());
        var tarball = Convert.FromBase64String(attachment.GetProperty("data").GetString()!);
        Assert.Equal(attachment.GetProperty("length").GetInt32(), tarball.Length);
        // A gzip stream.
        Assert.Equal(new byte[] { 0x1f, 0x8b }, tarball.Take(2));
    }
}
