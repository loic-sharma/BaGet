using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Vulnerability scanning of Maven repositories over HTTP (P7): an artifact and the compile and runtime dependencies of its POM
/// that state a literal version are checked against a fake OSV; the policy blocks deploys and downloads with a 403 and the reason.
/// </summary>
public class ScanningMavenTests
{
    private const string Repo = "/r/libs/maven/";
    private const int High = 2;

    private static readonly string Pom = """
        <project xmlns="http://maven.apache.org/POM/4.0.0">
          <modelVersion>4.0.0</modelVersion>
          <groupId>org.acme</groupId>
          <artifactId>lib</artifactId>
          <version>1.0</version>
          <dependencies>
            <dependency><groupId>org.bad</groupId><artifactId>bad</artifactId><version>2.3.4</version></dependency>
            <dependency><groupId>org.ranged</groupId><artifactId>ranged</artifactId><version>[1.5,2.0)</version></dependency>
            <dependency><groupId>org.prop</groupId><artifactId>prop</artifactId><version>${prop.version}</version></dependency>
            <dependency><groupId>junit</groupId><artifactId>junit</artifactId><version>4.0</version><scope>test</scope></dependency>
            <dependency><groupId>org.opt</groupId><artifactId>opt</artifactId><version>1.0</version><optional>true</optional></dependency>
          </dependencies>
        </project>
        """;

    private readonly ITestOutputHelper _output;

    public ScanningMavenTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private async Task<ScanningHarness> StartAsync()
    {
        var harness = await ScanningHarness.StartAsync(_output);
        await harness.App.CreateMavenRepositoryAsync("libs", cancellationToken: TestContext.Current.CancellationToken);

        return harness;
    }

    private static async Task<HttpResponseMessage> PutAsync(ScanningHarness harness, string path, string content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Repo + path) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("deployer:" + ScanningHarness.Key)));

        return await harness.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> GetAsync(ScanningHarness harness, string path)
        => harness.Client.GetAsync(Repo + path, TestContext.Current.CancellationToken);

    private static async Task<string> TextAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task AnArtifactAndTheLiteralVersionsOfItsRuntimeDependenciesAreScanned()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("Maven", "org.bad:bad", "2.3.4", "GHSA-maven", "CRITICAL", "2.3.5");
        harness.Osv.Add("Maven", "org.ranged:ranged", "1.5", "GHSA-ranged", "LOW");
        using (await PutAsync(harness, "org/acme/lib/1.0/lib-1.0.pom", Pom)) { }

        var result = (await harness.ScanAsync("libs", "org.acme:lib", "1.0"))!;

        Assert.Equal(2, result.FindingCount);
        Assert.Equal(3, result.Highest);
        var found = (await harness.ResultAsync("libs", "org.acme:lib", "1.0"))!;
        Assert.Equal(new[] { "org.bad:bad", "org.ranged:ranged" }, found.Findings!.Select(f => f.Component).OrderBy(c => c, StringComparer.Ordinal).ToArray());
        // The artifact, "bad" and the lower bound of "ranged": not the property version, the test scope or the optional one.
        Assert.Equal(3, harness.Osv.QueriedPackages);
    }

    [Fact]
    public async Task ABlockDeployPolicyRefusesAPomWhoseDependencyIsVulnerable()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("Maven", "org.bad:bad", "2.3.4", "GHSA-maven-deploy", "HIGH");
        await harness.SetPolicyAsync("libs", blockDeploy: High);

        using var refused = await PutAsync(harness, "org/acme/lib/1.0/lib-1.0.pom", Pom);
        using var absent = await GetAsync(harness, "org/acme/lib/1.0/lib-1.0.pom");
        using var other = await PutAsync(harness, "org/acme/lib/2.0/lib-2.0.pom", Pom.Replace("2.3.4", "2.3.5").Replace("<version>1.0</version>\n  <dependencies>", "<version>2.0</version>\n  <dependencies>"));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var reason = await TextAsync(refused);
        Assert.Contains("GHSA-maven-deploy", reason);
        Assert.Contains("org.bad:bad 2.3.4", reason);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    [Fact]
    public async Task ABlockDownloadPolicyRefusesTheFilesOfAVulnerableVersionButNotTheMetadata()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("Maven", "org.acme:lib", "1.0", "GHSA-maven-download", "CRITICAL");
        using (await PutAsync(harness, "org/acme/lib/1.0/lib-1.0.pom", Pom.Replace("org.bad", "org.fine"))) { }
        using (await PutAsync(harness, "org/acme/lib/1.0/lib-1.0.jar", "jar bytes")) { }
        await harness.ScanAsync("libs", "org.acme:lib", "1.0");
        await harness.SetPolicyAsync("libs", blockDownload: High);

        using var jar = await GetAsync(harness, "org/acme/lib/1.0/lib-1.0.jar");
        using var pom = await GetAsync(harness, "org/acme/lib/1.0/lib-1.0.pom");
        using var metadata = await GetAsync(harness, "org/acme/lib/maven-metadata.xml");

        Assert.Equal(HttpStatusCode.Forbidden, jar.StatusCode);
        Assert.Contains("GHSA-maven-download", await TextAsync(jar));
        Assert.Equal(HttpStatusCode.Forbidden, pom.StatusCode);
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
    }

    [Fact]
    public async Task ABrowserDeployIsRefusedByTheDeployPolicyToo()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("Maven", "org.acme:lib", "1.0", "GHSA-maven-page", "HIGH");
        await harness.SetPolicyAsync("libs", blockDeploy: High);

        using var browser = new Browser(harness.App);
        var fields = new System.Collections.Generic.Dictionary<string, string>
        {
            ["groupId"] = "org.acme",
            ["artifactId"] = "lib",
            ["version"] = "1.0",
            ["packaging"] = "jar",
            ["apiKey"] = ScanningHarness.Key,
        };
        using var response = await browser.PostFileAsync("r/libs/maven-upload", "file", "lib-1.0.jar", new System.IO.MemoryStream(Encoding.UTF8.GetBytes("jar")), fields);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("GHSA-maven-page", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
