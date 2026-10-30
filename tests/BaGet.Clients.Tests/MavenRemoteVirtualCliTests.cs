using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace BaGet.Clients.Tests;

/// <summary>
/// The real <c>mvn</c> (3.9, in <c>maven:3.9-eclipse-temurin-21</c>) against remote and virtual Maven repositories. The upstream of
/// a remote repository is a second real BaGet application (a local Maven repository on its own socket, filled by <c>mvn deploy</c>),
/// not the internet: the downstream process reaches it as <c>localhost</c>, the container reaches the downstream as
/// <c>host.docker.internal</c>. One test sends everything, plugins included, through a remote repository that proxies the real Maven
/// Central, and skips when the machine is offline. All skip without a Docker daemon.
/// </summary>
public class MavenRemoteVirtualCliTests
{
    private const string Source = "src";
    private const string Mirror = "mirror";
    private const string Mine = "mine";
    private const string All = "all";
    private const string GlobalKey = "global-key-for-cli";
    private const string Cache = "/m2cache";

    private static string Group() => "org.t" + Guid.NewGuid().ToString("N").Substring(0, 8);

    // settings.xml: the <server> id is the one -DrepositoryId and <repository> use. Maven 3.8.1+ blocks plain-http repositories
    // (a mirror named maven-default-http-blocker); the test servers speak http, so the blocker is replaced by a mirror of nothing.
    private static string Settings(string user, string password, string extraMirrors = "")
        => $"""
            cat > $HOME/settings.xml <<'XML'
            <settings><servers><server><id>baget</id><username>{user}</username><password>{password}</password></server></servers>
            <mirrors>{extraMirrors}<mirror><id>maven-default-http-blocker</id><mirrorOf>dummy</mirrorOf><url>http://0.0.0.0/</url></mirror></mirrors></settings>
            XML

            """;

    private static string Pom(string group, string artifact, string version)
        => $"""
            cat > pom.xml <<'XML'
            <project xmlns="http://maven.apache.org/POM/4.0.0"><modelVersion>4.0.0</modelVersion>
              <groupId>{group}</groupId><artifactId>{artifact}</artifactId><version>{version}</version><packaging>jar</packaging>
              <name>Test {artifact}</name><description>Deployed by the real mvn</description>
            </project>
            XML

            """;

    private static string Jar(string text) => $"mkdir -p c && echo '{text}' > c/data.txt && jar cf lib.jar -C c .\n";

    private static string Deploy(string url, string version, string group, string artifact, string text)
        => Pom(group, artifact, version) + Jar(text)
           + $"mvn -B -ntp -q -s $HOME/settings.xml -Dmaven.repo.local={Cache} deploy:deploy-file -Dfile=lib.jar -DpomFile=pom.xml -DrepositoryId=baget -Durl={url}\n";

    // A consumer project that lists the repository and depends on the artifacts, resolved into an empty local repository whose
    // folder is named by `tag` (so a second resolution in the same home downloads again).
    private static string Get(string url, string tag, params (string Group, string Artifact, string Version)[] dependencies)
        => $"""
            cat > pom.xml <<'XML'
            <project xmlns="http://maven.apache.org/POM/4.0.0"><modelVersion>4.0.0</modelVersion>
              <groupId>consumer</groupId><artifactId>app</artifactId><version>1</version>
              <repositories><repository><id>baget</id><url>{url}</url>
                <releases><enabled>true</enabled></releases><snapshots><enabled>true</enabled></snapshots></repository></repositories>
              <dependencies>{string.Concat(dependencies.Select(d => $"<dependency><groupId>{d.Group}</groupId><artifactId>{d.Artifact}</artifactId><version>{d.Version}</version></dependency>"))}</dependencies>
            </project>
            XML
            mvn -B -s $HOME/settings.xml -Dmaven.repo.local=$HOME/local-{tag} dependency:copy-dependencies -DoutputDirectory=$HOME/out-{tag}

            """;

    private static async Task<string> GetTextAsync(string url)
    {
        using var http = new HttpClient();
        return await http.GetStringAsync(url);
    }

    // An upstream: a real BaGet with a local Maven repository (releases and snapshots) whose artifacts were deployed by the real mvn.
    private static async Task<BaGetServer> StartUpstreamAsync(IContainer maven, params (string Group, string Artifact, string Version, string Text)[] artifacts)
    {
        var upstream = await BaGetServer.StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await upstream.CreateMavenRepositoryAsync(Source, policy: "Both");

        string script;
        // Each deploy runs in its own directory so the generated pom.xml and jar do not collide.
        script = Settings("deployer", GlobalKey) + string.Concat(artifacts.Select((a, i) =>
            $"mkdir -p d{i} && cd d{i}\n" + Deploy(upstream.MavenUrl(Source), a.Version, a.Group, a.Artifact, a.Text) + "cd ..\n"));
        var deployed = await ClientContainers.RunAsync(maven, script);
        Assert.True(deployed.ExitCode == 0, deployed.All);

        return upstream;
    }

    [Fact]
    public async Task ARemoteRepositoryResolvesThroughMvnKeepsTheFilesAndKeepsServingThemWhenTheUpstreamIsGone()
    {
        var maven = await ClientContainers.MavenAsync();
        var group = Group();
        var upstream = await StartUpstreamAsync(maven, (group, "lib", "1.0", "release bytes"));
        await using var down = await BaGetServer.StartAsync();
        await down.CreateRemoteMavenRepositoryAsync(Mirror, upstream.LocalMavenUrl(Source));
        var path = group.Replace('.', '/');

        var first = await ClientContainers.RunAsync(maven, Settings("x", "y") + Get(down.MavenUrl(Mirror), "one", (group, "lib", "1.0")) + $"ls $HOME/local-one/{path}/lib/1.0/\n");
        Assert.True(first.ExitCode == 0, first.All);
        Assert.True(first.Stdout.Contains("Downloaded from: baget") || first.Stdout.Contains("Downloaded from baget"), first.All);
        Assert.Contains("lib-1.0.jar", first.Stdout);

        // Kept in the remote repository, with the checksum the upstream publishes.
        Assert.Equal(new[] { "1.0" }, (await down.StoredMavenAsync(Mirror))[$"{group}:lib"]);
        var upstreamSha = (await GetTextAsync($"{upstream.LocalMavenUrl(Source)}/{path}/lib/1.0/lib-1.0.jar.sha1")).Trim();
        Assert.Equal(upstreamSha, (await GetTextAsync($"{down.LocalMavenUrl(Mirror)}/{path}/lib/1.0/lib-1.0.jar.sha1")).Trim());
        Assert.Contains("<version>1.0</version>", await GetTextAsync($"{down.LocalMavenUrl(Mirror)}/{path}/lib/maven-metadata.xml"));

        // The upstream goes away. A fresh client still resolves the kept version (its metadata is remembered and its files are
        // stored); a version never fetched cannot be had, and the client sees a failure, not a missing artifact.
        await upstream.DisposeAsync();
        var again = await ClientContainers.RunAsync(maven, Settings("x", "y") + Get(down.MavenUrl(Mirror), "two", (group, "lib", "1.0")) + $"ls $HOME/local-two/{path}/lib/1.0/\n");
        Assert.True(again.ExitCode == 0, again.All);
        Assert.Contains("lib-1.0.jar", again.Stdout);

        var missing = await ClientContainers.RunAsync(maven, Settings("x", "y") + Get(down.MavenUrl(Mirror), "three", (group, "lib", "2.0")) + "\n");
        Assert.NotEqual(0, missing.ExitCode);
        Assert.Contains("502", missing.All);
    }

    [Fact]
    public async Task ASnapshotFollowsTheUpstreamsNewestBuildThroughARemoteRepository()
    {
        var maven = await ClientContainers.MavenAsync();
        var group = Group();
        var upstream = await StartUpstreamAsync(maven, (group, "lib", "1.0-SNAPSHOT", "build one"));
        await using var _ = upstream;
        await using var down = await BaGetServer.StartAsync();
        // A snapshot's metadata moves with every deploy: the metadata period is zero so the next build is seen at once.
        await down.CreateRemoteMavenRepositoryAsync(Mirror, upstream.LocalMavenUrl(Source), s => s.MetadataTtlSeconds = 0);
        var path = group.Replace('.', '/');

        var first = await ClientContainers.RunAsync(maven, Settings("x", "y") + Get(down.MavenUrl(Mirror), "one", (group, "lib", "1.0-SNAPSHOT")) + $"ls $HOME/local-one/{path}/lib/1.0-SNAPSHOT/\n");
        Assert.True(first.ExitCode == 0, first.All);
        Assert.Matches(@"lib-1\.0-\d{8}\.\d{6}-1\.jar", first.Stdout);

        var second = await ClientContainers.RunAsync(maven, Settings("deployer", GlobalKey) + Deploy(upstream.MavenUrl(Source), "1.0-SNAPSHOT", group, "lib", "build two, longer"));
        Assert.True(second.ExitCode == 0, second.All);

        var again = await ClientContainers.RunAsync(maven, Settings("x", "y") + Get(down.MavenUrl(Mirror), "two", (group, "lib", "1.0-SNAPSHOT")) + $"ls $HOME/local-two/{path}/lib/1.0-SNAPSHOT/\n");
        Assert.True(again.ExitCode == 0, again.All);
        Assert.Matches(@"lib-1\.0-\d{8}\.\d{6}-2\.jar", again.Stdout);

        // Both builds were asked for by their timestamped names and are kept.
        Assert.Equal(new[] { "1.0-SNAPSHOT" }, (await down.StoredMavenAsync(Mirror))[$"{group}:lib"]);
    }

    [Fact]
    public async Task ADeployToARemoteRepositoryIsRefused()
    {
        var maven = await ClientContainers.MavenAsync();
        var upstream = await StartUpstreamAsync(maven, (Group(), "lib", "1.0", "x"));
        await using var _ = upstream;
        await using var down = await BaGetServer.StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await down.CreateRemoteMavenRepositoryAsync(Mirror, upstream.LocalMavenUrl(Source));

        var result = await ClientContainers.RunAsync(maven, Settings("deployer", GlobalKey) + Deploy(down.MavenUrl(Mirror), "1.0", Group(), "lib", "mine") + "\n");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("403", result.All);
        Assert.Empty(await down.StoredMavenAsync(Mirror));
    }

    [Fact]
    public async Task AVirtualRepositoryMergesTheLocalAndTheRemoteMemberAndDeploysToItsDeployRepository()
    {
        var maven = await ClientContainers.MavenAsync();
        var group = Group();
        var upstream = await StartUpstreamAsync(maven, (group, "remote-lib", "3.0", "from upstream"), (group, "shared", "1.0", "upstream one"), (group, "shared", "2.0", "upstream two"));
        await using var _ = upstream;
        await using var down = await BaGetServer.StartAsync(new Dictionary<string, string> { ["ApiKey"] = GlobalKey });
        await down.CreateMavenRepositoryAsync(Mine, policy: "Both");
        await down.CreateRemoteMavenRepositoryAsync(Mirror, upstream.LocalMavenUrl(Source));
        await down.CreateVirtualMavenRepositoryAsync(All, new[] { Mine, Mirror }, Mine);
        var url = down.MavenUrl(All);
        var path = group.Replace('.', '/');

        // Local artifacts go in through the virtual repository (to its deploy repository), then everything is read from it.
        var deploy = await ClientContainers.RunAsync(
            maven,
            Settings("deployer", GlobalKey)
            + "mkdir a && cd a\n" + Deploy(url, "1.0", group, "local-lib", "local bytes") + "cd ..\n"
            + "mkdir b && cd b\n" + Deploy(url, "1.0", group, "shared", "local shared") + "cd ..\n");
        Assert.True(deploy.ExitCode == 0, deploy.All);

        var resolve = await ClientContainers.RunAsync(
            maven,
            Settings("x", "y") + Get(url, "all", (group, "local-lib", "1.0"), (group, "remote-lib", "3.0"), (group, "shared", "1.0")));
        Assert.True(resolve.ExitCode == 0, resolve.All);
        Assert.Contains($"Copying artifact '{group}:local-lib:jar:1.0", resolve.Stdout);
        Assert.Contains($"Copying artifact '{group}:remote-lib:jar:3.0", resolve.Stdout);
        Assert.Contains($"Copying artifact '{group}:shared:jar:1.0", resolve.Stdout);

        // shared 1.0 comes from `mine` (first member), shared 2.0 only from the upstream; the version list is the union and
        // `release` follows the member order: mine names 1.0 as its highest.
        var shared = await GetTextAsync($"{down.LocalMavenUrl(All)}/{path}/shared/maven-metadata.xml");
        Assert.Contains("<version>1.0</version>", shared);
        Assert.Contains("<version>2.0</version>", shared);
        Assert.Contains("<release>1.0</release>", shared);
        Assert.Equal(
            (await GetTextAsync($"{down.LocalMavenUrl(Mine)}/{path}/shared/1.0/shared-1.0.jar.sha1")).Trim(),
            (await GetTextAsync($"{down.LocalMavenUrl(All)}/{path}/shared/1.0/shared-1.0.jar.sha1")).Trim());

        // The deploys landed in `mine`; the remote member kept only what was resolved through it.
        var mine = await down.StoredMavenAsync(Mine);
        Assert.Contains($"{group}:local-lib", mine.Keys);
        Assert.Contains($"{group}:shared", mine.Keys);
        var mirror = await down.StoredMavenAsync(Mirror);
        Assert.Equal(new[] { $"{group}:remote-lib" }, mirror.Keys.ToArray());
    }

    [Fact]
    public async Task ADownUpstreamIsAFailureTheClientSeesNotAMissingArtifact()
    {
        var maven = await ClientContainers.MavenAsync();
        var group = Group();
        var upstream = await StartUpstreamAsync(maven, (group, "lib", "1.0", "x"));
        await using var down = await BaGetServer.StartAsync();
        await down.CreateRemoteMavenRepositoryAsync(Mirror, upstream.LocalMavenUrl(Source));
        await upstream.DisposeAsync();

        var result = await ClientContainers.RunAsync(maven, Settings("x", "y") + Get(down.MavenUrl(Mirror), "one", (group, "lib", "1.0")) + "\n");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("502", result.All);
    }

    [Fact]
    public async Task MavenCentralThroughARemoteRepositoryResolvesPluginsAndDependenciesAndKeepsThem()
    {
        // The only test that needs the internet: the repository and the artifacts are the real Maven Central. The mirror of "*"
        // sends the plugin and its dependencies through the proxy too, so the plugin prefix metadata (group-level) is exercised.
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
        {
            try
            {
                using var ping = await http.GetAsync("https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-metadata.xml", TestContext.Current.CancellationToken);
                if (!ping.IsSuccessStatusCode) Assert.Skip("repo.maven.apache.org does not answer.");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                Assert.Skip("repo.maven.apache.org cannot be reached: " + e.GetBaseException().Message);
            }
        }

        var maven = await ClientContainers.MavenAsync();
        await using var down = await BaGetServer.StartAsync();
        await down.CreateRemoteMavenRepositoryAsync(Mirror, "https://repo.maven.apache.org/maven2/");
        var url = down.MavenUrl(Mirror);

        var mirror = $"<mirror><id>central-proxy</id><mirrorOf>*</mirrorOf><url>{url}</url></mirror>";
        var result = await ClientContainers.RunAsync(
            maven,
            Settings("x", "y", mirror)
            + "mvn -B -s $HOME/settings.xml -Dmaven.repo.local=$HOME/local-central dependency:get -Dartifact=org.apache.commons:commons-lang3:3.14.0 2>&1\n"
            + "ls $HOME/local-central/org/apache/commons/commons-lang3/3.14.0/\n");

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Contains("commons-lang3-3.14.0.jar", result.Stdout);
        Assert.Matches(new Regex(@"Downloaded from central-proxy: .*commons-lang3-3\.14\.0\.jar"), result.Stdout);

        var stored = await down.StoredMavenAsync(Mirror);
        Assert.Contains("3.14.0", stored["org.apache.commons:commons-lang3"]);
        Assert.Contains("org.apache.maven.plugins:maven-dependency-plugin", stored.Keys);
    }
}
