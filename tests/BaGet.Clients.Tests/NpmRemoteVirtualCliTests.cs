using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using static BaGet.Clients.Tests.NpmRegistryCliTests;

namespace BaGet.Clients.Tests;

/// <summary>
/// The real <c>npm</c> CLI against remote and virtual npm repositories. The upstream of a remote repository is a second real BaGet
/// application (a local npm repository on its own socket), not the internet: the downstream process reaches it as
/// <c>localhost</c>, the CLI reaches the downstream as <c>host.docker.internal</c>. One test talks to registry.npmjs.org itself
/// and skips when the machine is offline. All skip without a Docker daemon.
/// </summary>
public class NpmRemoteVirtualCliTests
{
    private const string Source = "src";
    private const string Mirror = "mirror";
    private const string Mine = "mine";
    private const string All = "all";

    // Publishes name@version to the repository of `server` the script's npmrc points at.
    private static string PublishScript(string dir, string name, string version, string tag = "")
        => Package(dir, name, version) + "npm publish " + tag + "\ncd ..\n";

    // An upstream registry with the packages published by the real npm CLI, so what the proxy fetches is what npm itself uploads.
    private static async Task<BaGetServer> StartUpstreamAsync(DotNet.Testcontainers.Containers.IContainer node, params (string Name, string Version)[] packages)
    {
        var upstream = await BaGetServer.StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await upstream.CreateNpmRepositoryAsync(Source);

        var script = Npmrc(upstream, GlobalKey, Source) + string.Concat(packages.Select((p, i) => PublishScript("p" + i, p.Name, p.Version)));
        var published = await RunAsync(node, script);
        Assert.True(published.ExitCode == 0, published.All);

        return upstream;
    }

    [Fact]
    public async Task ARemoteRepositoryInstallsThroughNpmCachesTheTarballAndKeepsServingItWhenTheUpstreamIsGone()
    {
        var node = await ClientContainers.NodeAsync();
        var upstream = await StartUpstreamAsync(node, ("left-pad", "1.0.0"), ("left-pad", "1.1.0"));
        await using var down = await BaGetServer.StartAsync();
        await down.CreateRemoteNpmRepositoryAsync(Mirror, upstream.LocalRegistryUrl(Source));
        var upstreamIntegrity = (await upstream.PackageAsync(Source, "left-pad"))!.Versions.Single(v => v.Version == "1.1.0").Integrity;

        var install = await RunAsync(node, Npmrc(down, null, Mirror) + """
            mkdir app && cd app
            echo '{"name":"app","version":"1.0.0"}' > package.json
            npm install left-pad
            node -e "console.log(require('left-pad')('x'))"
            echo "--- lock"
            grep -E '"(resolved|integrity)"' package-lock.json
            echo "--- view"
            npm view left-pad versions --json
            npm view left-pad dist-tags --json
            """);

        Assert.True(install.ExitCode == 0, install.All);
        Assert.Contains("left-pad@1.1.0:x", install.Stdout);
        Assert.Contains($"http://{down.ContainerAuthority}/r/{Mirror}/npm/left-pad/-/left-pad-1.1.0.tgz", install.Stdout);
        Assert.Contains(upstreamIntegrity, install.Stdout);
        var view = install.Stdout.Substring(install.Stdout.IndexOf("--- view", StringComparison.Ordinal));
        Assert.Contains("1.0.0", view);
        Assert.Contains("1.1.0", view);

        // Only what was downloaded is stored: 1.1.0, with the checksum the upstream announced.
        var stored = await down.StoredAsync(Mirror, "left-pad");
        var version = Assert.Single(stored!.Versions);
        Assert.Equal("1.1.0", version.Version);
        Assert.Equal(upstreamIntegrity, version.Integrity);

        // The upstream goes away. A fresh client still installs the stored version (the packument is remembered in memory, the
        // tarball is stored); a version never downloaded cannot be had.
        await upstream.DisposeAsync();
        var again = await RunAsync(node, Npmrc(down, null, Mirror) + """
            mkdir app && cd app
            echo '{"name":"app","version":"1.0.0"}' > package.json
            npm install left-pad@1.1.0
            node -e "console.log(require('left-pad')('y'))"
            """);
        Assert.True(again.ExitCode == 0, again.All);
        Assert.Contains("left-pad@1.1.0:y", again.Stdout);

        var missing = await RunAsync(node, Npmrc(down, null, Mirror) + """
            mkdir app && cd app
            echo '{"name":"app","version":"1.0.0"}' > package.json
            npm install left-pad@1.0.0 2>&1
            """);
        Assert.NotEqual(0, missing.ExitCode);
    }

    [Fact]
    public async Task AScopedPackageIsInstalledThroughARemoteRepositoryWithTheEncodedSlash()
    {
        var node = await ClientContainers.NodeAsync();
        var upstream = await StartUpstreamAsync(node, ("@acme/tool", "2.1.0"));
        await using var _ = upstream;
        await using var down = await BaGetServer.StartAsync();
        await down.CreateRemoteNpmRepositoryAsync(Mirror, upstream.LocalRegistryUrl(Source));

        var result = await RunAsync(node, $$"""
            printf '@acme:registry={{down.RegistryUrl(Mirror)}}\nupdate-notifier=false\nfund=false\naudit=false\n' > $HOME/.npmrc
            mkdir app && cd app
            echo '{"name":"app","version":"1.0.0"}' > package.json
            npm install @acme/tool
            node -e "console.log(require('@acme/tool')('z'))"
            npm view @acme/tool version
            """);

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Contains("@acme/tool@2.1.0:z", result.Stdout);
        Assert.Equal("2.1.0", Assert.Single((await down.StoredAsync(Mirror, "@acme/tool"))!.Versions).Version);
    }

    [Fact]
    public async Task APackageTheUpstreamDoesNotHaveIsNotFoundAndAPublishToARemoteRepositoryIsRefused()
    {
        var node = await ClientContainers.NodeAsync();
        var upstream = await StartUpstreamAsync(node, ("left-pad", "1.0.0"));
        await using var _ = upstream;
        await using var down = await BaGetServer.StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await down.CreateRemoteNpmRepositoryAsync(Mirror, upstream.LocalRegistryUrl(Source));

        var result = await RunAsync(node, Npmrc(down, GlobalKey, Mirror) + """
            npm view nothing-like-it version 2>&1
            echo "--- publish"
            """ + "\n" + Package("pub", "mine-too", "1.0.0") + "npm publish 2>&1\n");

        Assert.Contains("E404", result.Stdout);
        var publish = result.Stdout.Substring(result.Stdout.IndexOf("--- publish", StringComparison.Ordinal));
        Assert.Contains("403", publish);
        Assert.Null(await down.StoredAsync(Mirror, "mine-too"));
    }

    [Fact]
    public async Task ADownUpstreamIsAnErrorTheClientSeesNotAMissingPackage()
    {
        var node = await ClientContainers.NodeAsync();
        var upstream = await StartUpstreamAsync(node, ("left-pad", "1.0.0"));
        await using var down = await BaGetServer.StartAsync();
        await down.CreateRemoteNpmRepositoryAsync(Mirror, upstream.LocalRegistryUrl(Source));
        await upstream.DisposeAsync();

        var result = await RunAsync(node, Npmrc(down, null, Mirror) + "npm view left-pad version 2>&1\n");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("502", result.All);
        Assert.DoesNotContain("E404", result.All);
    }

    [Fact]
    public async Task AVirtualRepositoryMergesTheLocalAndTheRemoteMemberAndPublishesToItsDeployRepository()
    {
        var node = await ClientContainers.NodeAsync();
        var upstream = await StartUpstreamAsync(node, ("shared", "1.0.0"), ("shared", "2.0.0"), ("remote-lib", "3.0.0"));
        await using var _ = upstream;
        await using var down = await BaGetServer.StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await down.CreateNpmRepositoryAsync(Mine);
        await down.CreateRemoteNpmRepositoryAsync(Mirror, upstream.LocalRegistryUrl(Source));
        await down.CreateVirtualNpmRepositoryAsync(All, new[] { Mine, Mirror }, Mine);

        // Local packages go in through the virtual repository (to its deploy repository), then everything is read from it.
        var result = await RunAsync(node, Npmrc(down, GlobalKey, All)
            + PublishScript("a", "local-lib", "1.0.0")
            + PublishScript("b", "shared", "1.0.0")
            + """
              echo "--- install"
              mkdir app && cd app
              echo '{"name":"app","version":"1.0.0"}' > package.json
              npm install local-lib remote-lib shared
              node -e "console.log(require('local-lib')('a'), require('remote-lib')('b'), require('shared')('c'))"
              grep -E '"resolved"' package-lock.json
              cd ..
              echo "--- shared"
              npm view shared versions --json
              npm view shared dist-tags --json
              echo "--- search"
              npm search lib --json
              """);

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Contains("local-lib@1.0.0:a", result.Stdout);
        Assert.Contains("remote-lib@3.0.0:b", result.Stdout);
        Assert.Contains($"http://{down.ContainerAuthority}/r/{All}/npm/remote-lib/-/remote-lib-3.0.0.tgz", result.Stdout);
        Assert.Contains($"http://{down.ContainerAuthority}/r/{All}/npm/local-lib/-/local-lib-1.0.0.tgz", result.Stdout);

        var shared = result.Stdout.Substring(result.Stdout.IndexOf("--- shared", StringComparison.Ordinal));
        Assert.Contains("1.0.0", shared);
        Assert.Contains("2.0.0", shared);
        // `mine` is the first member and has latest = 1.0.0: member order decides, not the highest version.
        Assert.Contains("\"latest\": \"1.0.0\"", shared);
        Assert.Contains("local-lib", shared);

        // The publishes landed in the deploy repository; the remote member stored only what was downloaded.
        Assert.NotNull(await down.StoredAsync(Mine, "local-lib"));
        Assert.NotNull(await down.StoredAsync(Mine, "shared"));
        Assert.Null(await down.StoredAsync(Mirror, "local-lib"));
        Assert.Equal("3.0.0", Assert.Single((await down.StoredAsync(Mirror, "remote-lib"))!.Versions).Version);
    }

    [Fact]
    public async Task RegistryNpmjsOrgThroughARemoteRepository()
    {
        // The only test that needs the internet: the packages and the registry are real. It skips when the machine is offline.
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
        {
            try
            {
                using var ping = await http.GetAsync("https://registry.npmjs.org/-/ping", TestContext.Current.CancellationToken);
                if (!ping.IsSuccessStatusCode) Assert.Skip("registry.npmjs.org does not answer.");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                Assert.Skip("registry.npmjs.org cannot be reached: " + e.GetBaseException().Message);
            }
        }

        var node = await ClientContainers.NodeAsync();
        await using var down = await BaGetServer.StartAsync();
        await down.CreateRemoteNpmRepositoryAsync(Mirror, "https://registry.npmjs.org/");

        var result = await RunAsync(node, Npmrc(down, null, Mirror) + """
            mkdir app && cd app
            echo '{"name":"app","version":"1.0.0"}' > package.json
            npm install is-odd@3.0.1
            node -e "console.log(require('is-odd')(3))"
            grep -E '"resolved"' package-lock.json
            """);

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Contains("true", result.Stdout);
        Assert.Contains($"http://{down.ContainerAuthority}/r/{Mirror}/npm/is-odd/-/is-odd-3.0.1.tgz", result.Stdout);
        Assert.NotNull(await down.StoredAsync(Mirror, "is-odd"));
        Assert.NotNull(await down.StoredAsync(Mirror, "is-number"));
    }
}
