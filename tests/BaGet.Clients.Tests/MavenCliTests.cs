using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace BaGet.Clients.Tests;

/// <summary>
/// The real <c>mvn</c> (3.9, in <c>maven:3.9-eclipse-temurin-21</c>) against the real BaGet application on a real socket:
/// <c>deploy:deploy-file</c> with the credentials of <c>settings.xml</c> (HTTP Basic after the 401 challenge, B3), then
/// <c>dependency:get</c> into a clean local repository, release and snapshot (timestamped) resolution, the release/snapshot
/// policy, immutable releases and a private repository. The plugins come from Maven Central (the container needs the internet)
/// and are cached in one directory shared by the tests, while every test uses its own group so nothing resolves from a cache.
/// The tests skip without a Docker daemon.
/// </summary>
public class MavenCliTests
{
    private const string Repository = "libs";
    private const string GlobalKey = "global-key-for-cli";
    private const string Cache = "/m2cache";

    private static string Group() => "org.t" + Guid.NewGuid().ToString("N").Substring(0, 8);

    // settings.xml: the <server> id is the one -DrepositoryId and <repository> use. Maven 3.8.1+ blocks plain-http repositories
    // (a mirror named maven-default-http-blocker); the test server speaks http, so the blocker is replaced by a mirror of nothing.
    private static string Settings(string user, string password)
        => $"""
            cat > $HOME/settings.xml <<'XML'
            <settings><servers><server><id>baget</id><username>{user}</username><password>{password}</password></server></servers>
            <mirrors><mirror><id>maven-default-http-blocker</id><mirrorOf>dummy</mirrorOf><url>http://0.0.0.0/</url></mirror></mirrors></settings>
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

    // A jar made with the JDK's own jar tool.
    private static string Jar(string text) => $"mkdir -p c && echo '{text}' > c/data.txt && jar cf lib.jar -C c .\n";

    private static string Deploy(string url, string version, string group, string artifact, string text)
        => Pom(group, artifact, version) + Jar(text)
           + $"mvn -B -ntp -q -s $HOME/settings.xml -Dmaven.repo.local={Cache} deploy:deploy-file -Dfile=lib.jar -DpomFile=pom.xml -DrepositoryId=baget -Durl={url}\n";

    // A consumer project that lists the repository and depends on the artifact, as the repository page's snippet tells users to
    // (the id of <repository> is the id of the settings.xml <server>); resolved into an empty local repository.
    private static string Get(string url, string group, string artifact, string version)
        => $"""
            cat > pom.xml <<'XML'
            <project xmlns="http://maven.apache.org/POM/4.0.0"><modelVersion>4.0.0</modelVersion>
              <groupId>consumer</groupId><artifactId>app</artifactId><version>1</version>
              <repositories><repository><id>baget</id><url>{url}</url>
                <releases><enabled>true</enabled></releases><snapshots><enabled>true</enabled></snapshots></repository></repositories>
              <dependencies><dependency><groupId>{group}</groupId><artifactId>{artifact}</artifactId><version>{version}</version></dependency></dependencies>
            </project>
            XML
            mvn -B -s $HOME/settings.xml -Dmaven.repo.local=$HOME/local dependency:copy-dependencies -DoutputDirectory=$HOME/out

            """;

    private static async Task<(BaGetServer Server, IContainer Maven)> StartAsync(string policy = "Releases", bool redeploy = false, bool open = true, IDictionary<string, string>? settings = null)
    {
        var maven = await ClientContainers.MavenAsync();
        var all = new Dictionary<string, string> { ["ApiKey"] = GlobalKey };
        foreach (var pair in settings ?? new Dictionary<string, string>()) all[pair.Key] = pair.Value;
        var server = await BaGetServer.StartAsync(all);
        await server.CreateMavenRepositoryAsync(Repository, policy, redeploy, anonymousRead: open);

        return (server, maven);
    }

    private static async Task<string> GetTextAsync(string url)
    {
        using var http = new HttpClient();
        return await http.GetStringAsync(url);
    }

    [Fact]
    public async Task AReleaseDeployedWithMvnIsResolvedByMvnAndServedWithItsChecksumsAndMetadata()
    {
        var (server, maven) = await StartAsync();
        await using var _ = server;
        var group = Group();
        var path = group.Replace('.', '/');

        var deploy = await ClientContainers.RunAsync(maven, Settings("deployer", GlobalKey) + Deploy(server.MavenUrl(Repository), "1.0", group, "lib", "release bytes"));
        Assert.True(deploy.ExitCode == 0, deploy.All);

        var get = await ClientContainers.RunAsync(maven, Settings("x", "y") + Get(server.MavenUrl(Repository), group, "lib", "1.0")
            + $"ls $HOME/local/{path}/lib/1.0/\n");
        Assert.True(get.ExitCode == 0, get.All);
        Assert.True(get.Stdout.Contains("Downloaded from: baget") || get.Stdout.Contains("Downloaded from baget"), get.All);
        Assert.Contains("lib-1.0.jar", get.Stdout);

        var baseUrl = server.LocalMavenUrl(Repository) + "/" + path + "/lib";
        Assert.Contains("<version>1.0</version>", await GetTextAsync(baseUrl + "/maven-metadata.xml"));
        Assert.Equal(40, (await GetTextAsync(baseUrl + "/1.0/lib-1.0.jar.sha1")).Trim().Length);
        Assert.Contains("Test lib", await GetTextAsync(baseUrl + "/1.0/lib-1.0.pom"));
    }

    [Fact]
    public async Task ASnapshotIsDeployedTwiceAndMvnResolvesTheNewestTimestampedBuild()
    {
        var (server, maven) = await StartAsync(policy: "Both");
        await using var _ = server;
        var group = Group();
        var url = server.MavenUrl(Repository);

        var first = await ClientContainers.RunAsync(maven, Settings("deployer", GlobalKey) + Deploy(url, "1.0-SNAPSHOT", group, "lib", "build one"));
        Assert.True(first.ExitCode == 0, first.All);
        var second = await ClientContainers.RunAsync(maven, Settings("deployer", GlobalKey) + Deploy(url, "1.0-SNAPSHOT", group, "lib", "build two, longer"));
        Assert.True(second.ExitCode == 0, second.All);

        var path = group.Replace('.', '/');
        var versionMetadata = await GetTextAsync($"{server.LocalMavenUrl(Repository)}/{path}/lib/1.0-SNAPSHOT/maven-metadata.xml");
        Assert.Contains("<timestamp>", versionMetadata);
        Assert.Contains("<buildNumber>2</buildNumber>", versionMetadata);

        var get = await ClientContainers.RunAsync(maven, Settings("x", "y") + Get(url, group, "lib", "1.0-SNAPSHOT")
            + $"ls $HOME/local/{path}/lib/1.0-SNAPSHOT/\n");
        Assert.True(get.ExitCode == 0, get.All);
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(get.Stdout, @"lib-1\.0-\d{8}\.\d{6}-2\.jar"), get.All);
    }

    [Fact]
    public async Task TheReleasePolicyAndAnExistingReleaseRefuseADeployWith409()
    {
        var (server, maven) = await StartAsync();
        await using var _ = server;
        var group = Group();
        var url = server.MavenUrl(Repository);

        var release = await ClientContainers.RunAsync(maven, Settings("deployer", GlobalKey) + Deploy(url, "1.0", group, "lib", "once"));
        Assert.True(release.ExitCode == 0, release.All);

        var again = await ClientContainers.RunAsync(maven, Settings("deployer", GlobalKey) + Deploy(url, "1.0", group, "lib", "changed"));
        Assert.NotEqual(0, again.ExitCode);
        Assert.Contains("409", again.All);

        var snapshot = await ClientContainers.RunAsync(maven, Settings("deployer", GlobalKey) + Deploy(url, "2.0-SNAPSHOT", group, "lib", "snap"));
        Assert.NotEqual(0, snapshot.ExitCode);
        Assert.Contains("409", snapshot.All);
    }

    [Fact]
    public async Task APrivateRepositoryAsksForCredentialsAndMvnSendsTheOnesOfSettingsXml()
    {
        var (server, maven) = await StartAsync(open: false, settings: new Dictionary<string, string> { ["Accounts:Enabled"] = "true" });
        await using var _ = server;
        await server.CreateUserAsync("alice");
        await server.GrantReadAsync("alice", Repository);
        var key = await server.CreateKeyAsync("alice", Repository);
        var group = Group();
        var url = server.MavenUrl(Repository);

        // The global ApiKey (a superuser) deploys; alice only reads.
        var deploy = await ClientContainers.RunAsync(maven, Settings("deployer", GlobalKey) + Deploy(url, "1.0", group, "lib", "private"));
        Assert.True(deploy.ExitCode == 0, deploy.All);

        var wrong = await ClientContainers.RunAsync(maven, Settings("nobody", "wrong") + Get(url, group, "lib", "1.0"));
        Assert.NotEqual(0, wrong.ExitCode);
        Assert.True(wrong.All.Contains("401"), wrong.All);

        var signedIn = await ClientContainers.RunAsync(maven, Settings("alice", key) + Get(url, group, "lib", "1.0"));
        Assert.True(signedIn.ExitCode == 0, signedIn.All);
        Assert.True(signedIn.Stdout.Contains("Downloaded from: baget") || signedIn.Stdout.Contains("Downloaded from baget"), signedIn.All);

        using var http = new HttpClient();
        using var noCredentials = await http.GetAsync($"{server.LocalMavenUrl(Repository)}/{group.Replace('.', '/')}/lib/1.0/lib-1.0.pom", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, noCredentials.StatusCode);
        Assert.Contains("Basic", noCredentials.Headers.WwwAuthenticate.ToString());
    }
}
