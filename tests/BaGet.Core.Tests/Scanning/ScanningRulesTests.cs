using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Xunit;

namespace BaGet.Core.Tests.Scanning;

/// <summary>
/// The rules of vulnerability scanning that need no server: severities, version order, OSV record matching, Trivy reports, the
/// components of each format, policy values and the offline OSV copy.
/// </summary>
public class ScanningRulesTests
{
    // ---- severities ----

    [Theory]
    [InlineData("LOW", 0)]
    [InlineData("low", 0)]
    [InlineData("MODERATE", 1)]
    [InlineData("MEDIUM", 1)]
    [InlineData(" High ", 2)]
    [InlineData("CRITICAL", 3)]
    public void AdvisorySourcesNameTheirSeveritiesWithTheUsualWords(string text, int expected)
    {
        Assert.True(ScanSeverities.TryParseName(text, out var severity));
        Assert.Equal(expected, severity);
    }

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnratedFindingCountsAsModerate(string? text)
    {
        Assert.False(ScanSeverities.TryParseName(text, out var severity));
        Assert.Equal(ScanSeverities.Unrated, severity);
        Assert.Equal(1, severity);
    }

    [Theory]
    [InlineData("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H", 9.8)]
    [InlineData("CVSS:3.1/AV:N/AC:L/PR:N/UI:R/S:C/C:L/I:L/A:N", 6.1)]
    [InlineData("CVSS:3.1/AV:L/AC:L/PR:L/UI:N/S:U/C:H/I:N/A:N", 5.5)]
    [InlineData("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:C/C:H/I:H/A:H", 10.0)]
    [InlineData("CVSS:3.0/AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:N", 0.0)]
    public void ACvssV3VectorGivesItsBaseScore(string vector, double expected)
    {
        Assert.Equal(expected, ScanSeverities.CvssV3BaseScore(vector));
    }

    [Theory]
    [InlineData("CVSS:4.0/AV:N/AC:L/AT:N/PR:N/UI:N/VC:H/VI:H/VA:H/SC:N/SI:N/SA:N")]
    [InlineData("CVSS:3.1/AV:N/AC:L")]
    [InlineData("CVSS:3.1/AV:X/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H")]
    [InlineData("garbage")]
    [InlineData(null)]
    public void AVectorThatIsNotCompleteCvssV3HasNoScore(string? vector)
    {
        Assert.Null(ScanSeverities.CvssV3BaseScore(vector));
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(3.9, 0)]
    [InlineData(4.0, 1)]
    [InlineData(6.9, 1)]
    [InlineData(7.0, 2)]
    [InlineData(8.9, 2)]
    [InlineData(9.0, 3)]
    [InlineData(10.0, 3)]
    public void AScoreMapsToTheQualitativeRating(double score, int severity)
    {
        Assert.Equal(severity, ScanSeverities.FromScore(score));
    }

    // ---- version order ----

    [Theory]
    [InlineData("NuGet", "1.0.0", "1.0.1", -1)]
    [InlineData("NuGet", "1.0.0-beta", "1.0.0", -1)]
    [InlineData("npm", "1.10.0", "1.9.0", 1)]
    [InlineData("npm", "2.0.0", "2.0.0", 0)]
    [InlineData("Maven", "1.0", "1.0.0", 0)]
    [InlineData("Maven", "1.10", "1.9", 1)]
    [InlineData("Maven", "1.0-rc1", "1.0", -1)]
    [InlineData("Maven", "1.0-SNAPSHOT", "1.0", -1)]
    [InlineData("Maven", "1.0-alpha", "1.0-beta", -1)]
    [InlineData("Maven", "1.0-sp1", "1.0", 1)]
    [InlineData("Maven", "1.0.Final", "1.0", 0)]
    [InlineData("Maven", "2.9.10.1", "2.9.10", 1)]
    [InlineData("Maven", "2.0-jre", "2.0", 1)]
    [InlineData("Maven", "2.0-android", "2.0-jre", -1)]
    [InlineData("Maven", "2.0-jre", "2.0-sp", 1)]
    [InlineData("Maven", "1.0.١", "1.0", 1)]
    public void VersionsAreOrderedTheWayTheirEcosystemOrdersThem(string ecosystem, string left, string right, int expected)
    {
        Assert.Equal(expected, Math.Sign(OsvVersions.Compare(ecosystem, left, right)));
        Assert.Equal(-expected, Math.Sign(OsvVersions.Compare(ecosystem, right, left)));
    }

    // ---- OSV records ----

    private static OsvAdvisory Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        return OsvAdvisory.Parse(document.RootElement)!;
    }

    private static string Record(string affected, string extra = "") =>
        "{\"id\":\"GHSA-test\"," + extra + "\"affected\":[" + affected + "]}";

    private static string Range(string ecosystem, string name, string events, string type = "SEMVER") =>
        "{\"package\":{\"ecosystem\":\"" + ecosystem + "\",\"name\":\"" + name + "\"},\"ranges\":[{\"type\":\"" + type + "\",\"events\":[" + events + "]}]}";

    [Fact]
    public void ARangeFromIntroducedToFixedCoversTheVersionsBetweenAndNamesTheFix()
    {
        var advisory = Parse(Record(Range("npm", "lodash", "{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}")));

        Assert.True(advisory.Affects(new ScanComponent("npm", "lodash", "4.17.20"), out var fixedIn));
        Assert.Equal("4.17.21", fixedIn);
        Assert.True(advisory.Affects(new ScanComponent("npm", "lodash", "0.0.1"), out _));
        Assert.False(advisory.Affects(new ScanComponent("npm", "lodash", "4.17.21"), out var none));
        Assert.Null(none);
        Assert.False(advisory.Affects(new ScanComponent("npm", "lodash", "5.0.0"), out _));
    }

    [Fact]
    public void SeveralRangesAndLastAffectedAreEvaluatedInVersionOrderWhateverOrderTheyAreWritten()
    {
        var advisory = Parse(Record(
            "{\"package\":{\"ecosystem\":\"NuGet\",\"name\":\"Acme\"},\"ranges\":["
            + "{\"type\":\"ECOSYSTEM\",\"events\":[{\"fixed\":\"1.5.0\"},{\"introduced\":\"1.0.0\"}]},"
            + "{\"type\":\"ECOSYSTEM\",\"events\":[{\"introduced\":\"2.0.0\"},{\"last_affected\":\"2.3.0\"}]}]}"));

        bool Affects(string version) => advisory.Affects(new ScanComponent("NuGet", "Acme", version), out _);

        Assert.False(Affects("0.9.0"));
        Assert.True(Affects("1.0.0"));
        Assert.True(Affects("1.4.9"));
        Assert.False(Affects("1.5.0"));
        Assert.False(Affects("1.9.0"));
        Assert.True(Affects("2.0.0"));
        Assert.True(Affects("2.3.0"));
        Assert.False(Affects("2.3.1"));
        Assert.True(advisory.Affects(new ScanComponent("NuGet", "Acme", "1.2.0"), out var fixedIn));
        Assert.Equal("1.5.0", fixedIn);
    }

    [Fact]
    public void AnExplicitVersionListAndTheNameCaseAreHonouredAndAGitRangeIsIgnored()
    {
        var advisory = Parse(Record(
            "{\"package\":{\"ecosystem\":\"Maven\",\"name\":\"org.acme:Lib\"},\"versions\":[\"3.0.0\"],\"ranges\":[{\"type\":\"GIT\",\"events\":[{\"introduced\":\"0\"}]}]}"));

        Assert.True(advisory.Affects(new ScanComponent("Maven", "org.acme:lib", "3.0.0"), out _));
        Assert.False(advisory.Affects(new ScanComponent("Maven", "org.acme:lib", "3.0.1"), out _));
        Assert.False(advisory.Affects(new ScanComponent("npm", "org.acme:lib", "3.0.0"), out _));
        Assert.False(advisory.Affects(new ScanComponent("Maven", "org.acme:other", "3.0.0"), out _));
    }

    [Fact]
    public void AWithdrawnRecordAffectsNothing()
    {
        var advisory = Parse(Record(Range("npm", "x", "{\"introduced\":\"0\"}"), "\"withdrawn\":\"2024-01-01T00:00:00Z\","));

        Assert.True(advisory.Withdrawn);
        Assert.False(advisory.Affects(new ScanComponent("npm", "x", "1.0.0"), out _));
    }

    [Fact]
    public void ARecordWithoutAnIdIsNotARecord()
    {
        using var document = JsonDocument.Parse("{\"summary\":\"no id\"}");
        Assert.Null(OsvAdvisory.Parse(document.RootElement));
    }

    [Fact]
    public void TheSeverityComesFromTheDatabaseThenCvssThenTheEcosystemThenModerate()
    {
        var named = Parse(Record("", "\"database_specific\":{\"severity\":\"CRITICAL\"},\"severity\":[{\"type\":\"CVSS_V3\",\"score\":\"CVSS:3.1/AV:L/AC:L/PR:L/UI:N/S:U/C:H/I:N/A:N\"}],"));
        var cvss = Parse(Record("", "\"severity\":[{\"type\":\"CVSS_V4\",\"score\":\"CVSS:4.0/AV:N\"},{\"type\":\"CVSS_V3\",\"score\":\"CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H\"}],"));
        var legacy = Parse(Record("{\"package\":{\"ecosystem\":\"npm\",\"name\":\"x\"},\"ecosystem_specific\":{\"severity\":\"LOW\"}}"));
        var none = Parse(Record(""));

        Assert.Equal(3, named.Severity);
        Assert.Equal(3, cvss.Severity);
        Assert.Equal(0, legacy.Severity);
        Assert.Equal(ScanSeverities.Unrated, none.Severity);
    }

    [Fact]
    public void TheLinkIsTheAdvisoryReferenceThenAWebOneThenTheOsvPageAndNeverANonHttpAddress()
    {
        var advisory = Parse(Record("", "\"references\":[{\"type\":\"WEB\",\"url\":\"https://example.test/web\"},{\"type\":\"ADVISORY\",\"url\":\"https://example.test/advisory\"}],"));
        var web = Parse(Record("", "\"references\":[{\"type\":\"WEB\",\"url\":\"https://example.test/web\"}],"));
        var unsafeOnly = Parse(Record("", "\"references\":[{\"type\":\"ADVISORY\",\"url\":\"javascript:alert(1)\"}],"));

        Assert.Equal("https://example.test/advisory", advisory.Url);
        Assert.Equal("https://example.test/web", web.Url);
        Assert.Equal("https://osv.dev/vulnerability/GHSA-test", unsafeOnly.Url);
    }

    [Fact]
    public void TheSummaryFallsBackToTheFirstLineOfTheDetails()
    {
        var advisory = Parse(Record("", "\"details\":\"First line.\\nSecond line.\","));

        Assert.Equal("First line.", advisory.Summary);
    }

    // ---- Trivy ----

    [Fact]
    public void ATrivyReportListsTheVulnerabilitiesOfEveryResult()
    {
        var hits = TrivyReport.Parse("""
            {"SchemaVersion":2,"Results":[
              {"Target":"alpine 3.18","Vulnerabilities":[
                {"VulnerabilityID":"CVE-2024-1","PkgName":"openssl","InstalledVersion":"3.1.0","FixedVersion":"3.1.1","Severity":"CRITICAL","Title":"Overflow","PrimaryURL":"https://avd.aquasec.com/nvd/cve-2024-1"},
                {"VulnerabilityID":"CVE-2024-2","PkgName":"zlib","InstalledVersion":"1.2.0","Severity":"MEDIUM","Description":"A long description"}]},
              {"Target":"app/package-lock.json"},
              {"Target":"jar","Vulnerabilities":[{"VulnerabilityID":"GHSA-x","PkgName":"log4j","InstalledVersion":"2.14.0","Severity":"UNKNOWN","PrimaryURL":"ftp://nope"}]}]}
            """);

        Assert.Equal(3, hits.Count);
        Assert.Equal(new ScanHit("CVE-2024-1", "openssl", "3.1.0", 3, "Overflow", "https://avd.aquasec.com/nvd/cve-2024-1", "3.1.1"), hits[0]);
        Assert.Equal(1, hits[1].Severity);
        Assert.Equal("A long description", hits[1].Summary);
        Assert.Null(hits[1].FixedVersion);
        Assert.StartsWith("https://avd.aquasec.com/nvd/", hits[1].Url);
        Assert.Equal(ScanSeverities.Unrated, hits[2].Severity);
        Assert.StartsWith("https://", hits[2].Url);
    }

    [Theory]
    [InlineData("{\"Results\":null}", 0)]
    [InlineData("{}", 0)]
    public void ATrivyReportWithoutVulnerabilitiesIsEmpty(string json, int count)
    {
        Assert.Equal(count, TrivyReport.Parse(json).Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void TextThatIsNotATrivyReportIsRefused(string text)
    {
        Assert.Throws<ScanUnavailableException>(() => TrivyReport.Parse(text));
    }

    // ---- components ----

    [Fact]
    public void ANuGetPackageIsItselfAndTheLowestVersionOfEachDependencyRange()
    {
        var components = NuGetScanComponents.Of("Acme.App", "1.0.0.0", new (string?, string?)[]
        {
            ("Json", "[9.0.1,)"),
            ("Json", "9.0.1"),
            ("Upper", "(,2.0)"),
            ("Empty", null),
            (null, "1.0"),
            ("Bad", "not a range"),
        });

        Assert.Equal(new[] { new ScanComponent("NuGet", "Acme.App", "1.0.0"), new ScanComponent("NuGet", "Json", "9.0.1") }, components);
    }

    [Fact]
    public void AnNpmVersionIsItselfAndItsDependenciesThatNameOneExactVersion()
    {
        var manifest = JsonNode.Parse("""{"dependencies":{"exact":"1.2.3","caret":"^1.2.0","tilde":"~1.2","star":"*","tag":"latest","url":"git+https://x/y.git","pre":"2.0.0-beta.1"},"devDependencies":{"dev":"1.0.0"}}""") as JsonObject;

        var components = NpmScanComponents.Of("app", "1.0.0", manifest);

        Assert.Equal(
            new[] { new ScanComponent("npm", "app", "1.0.0"), new ScanComponent("npm", "exact", "1.2.3"), new ScanComponent("npm", "pre", "2.0.0-beta.1") },
            components);
        Assert.Single(NpmScanComponents.Of("app", "1.0.0", null));
    }

    [Fact]
    public void AMavenVersionIsItselfAndTheLiteralVersionsOfItsRuntimeDependencies()
    {
        using var pom = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
            <project>
              <groupId>org.acme</groupId><artifactId>lib</artifactId><version>1.0</version>
              <dependencies>
                <dependency><groupId>a</groupId><artifactId>plain</artifactId><version>1.2.3</version></dependency>
                <dependency><groupId>a</groupId><artifactId>runtime</artifactId><version>2.0</version><scope>runtime</scope></dependency>
                <dependency><groupId>a</groupId><artifactId>range</artifactId><version>[1.5,2.0)</version></dependency>
                <dependency><groupId>a</groupId><artifactId>open</artifactId><version>(,3.0]</version></dependency>
                <dependency><groupId>a</groupId><artifactId>prop</artifactId><version>${v}</version></dependency>
                <dependency><groupId>a</groupId><artifactId>managed</artifactId></dependency>
                <dependency><groupId>a</groupId><artifactId>test</artifactId><version>1.0</version><scope>test</scope></dependency>
                <dependency><groupId>a</groupId><artifactId>provided</artifactId><version>1.0</version><scope>provided</scope></dependency>
                <dependency><groupId>a</groupId><artifactId>optional</artifactId><version>1.0</version><optional>true</optional></dependency>
              </dependencies>
            </project>
            """));

        var components = MavenScanComponents.Of("org.acme", "lib", "1.0", MavenPom.TryParse(pom));

        Assert.Equal(
            new[]
            {
                new ScanComponent("Maven", "org.acme:lib", "1.0"),
                new ScanComponent("Maven", "a:plain", "1.2.3"),
                new ScanComponent("Maven", "a:runtime", "2.0"),
                new ScanComponent("Maven", "a:range", "1.5"),
            },
            components);
    }

    // ---- policy values ----

    [Fact]
    public void APolicyWithNothingSwitchedOnIsEmptyAndThresholdsAreZeroToThree()
    {
        Assert.True(new ScanPolicyValues(null, null, null, false).IsEmpty);
        Assert.False(new ScanPolicyValues(null, null, null, true).IsEmpty);
        Assert.False(new ScanPolicyValues(0, null, null, false).IsEmpty);
        Assert.Null(new ScanPolicyValues(0, 3, 2, true).Validate());
        Assert.NotNull(new ScanPolicyValues(4, null, null, false).Validate());
        Assert.NotNull(new ScanPolicyValues(null, -1, null, false).Validate());
        Assert.Contains("deploy", new ScanPolicyValues(null, null, 9, false).Validate());
    }

    // ---- the offline copy of OSV ----

    private sealed class Options : IOptionsSnapshot<ScanningOptions>
    {
        public Options(ScanningOptions value) { Value = value; }

        public ScanningOptions Value { get; }

        public ScanningOptions Get(string? name) => Value;
    }

    private static readonly string LodashRecord = Record(Range("npm", "lodash", "{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}"), "\"database_specific\":{\"severity\":\"HIGH\"},");

    private static OsvVulnerabilitySource Source(string? offlinePath, OsvState? state = null)
        => new(new Options(new ScanningOptions { OsvOfflinePath = offlinePath }), state ?? new OsvState());

    [Fact]
    public async Task AnOfflineDirectoryIsSearchedRecursivelyAndNeverTouchesTheNetwork()
    {
        var directory = Directory.CreateTempSubdirectory("osv-offline").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "npm"));
            File.WriteAllText(Path.Combine(directory, "npm", "GHSA-test.json"), LodashRecord);
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{ not json");
            var lodash = new ScanComponent("npm", "Lodash", "4.17.20");
            var fixedVersion = new ScanComponent("npm", "lodash", "4.17.21");

            var answers = await Source(directory).QueryAsync(new[] { lodash, fixedVersion }, TestContext.Current.CancellationToken);

            var hit = Assert.Single(answers[lodash]);
            Assert.Equal(new VulnerabilityHit("GHSA-test", null, 2, "https://osv.dev/vulnerability/GHSA-test", "4.17.21"), hit);
            Assert.Empty(answers[fixedVersion]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AnOfflineZipIsReadAndAReplacedCopyIsPickedUpOnceTheStateIsCleared()
    {
        var directory = Directory.CreateTempSubdirectory("osv-zip").FullName;
        try
        {
            var zip = Path.Combine(directory, "all.zip");
            void Write(string record)
            {
                if (File.Exists(zip)) File.Delete(zip);
                using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
                using var writer = new StreamWriter(archive.CreateEntry("GHSA-test.json").Open());
                writer.Write(record);
            }

            Write(LodashRecord);
            var state = new OsvState();
            var component = new ScanComponent("npm", "lodash", "4.17.20");
            Assert.Single((await Source(zip, state).QueryAsync(new[] { component }, TestContext.Current.CancellationToken))[component]);

            Write(Record(Range("npm", "other", "{\"introduced\":\"0\"}")));
            state.Clear();
            Assert.Empty((await Source(zip, state).QueryAsync(new[] { component }, TestContext.Current.CancellationToken))[component]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AMissingOfflinePathIsReportedNotTreatedAsNoVulnerabilities()
    {
        var missing = Path.Combine(Path.GetTempPath(), "osv-does-not-exist-" + Guid.NewGuid().ToString("N"));

        var error = await Assert.ThrowsAsync<ScanUnavailableException>(
            () => Source(missing).QueryAsync(new[] { new ScanComponent("npm", "x", "1.0.0") }, TestContext.Current.CancellationToken));

        Assert.Contains("does not exist", error.Message);
    }

    [Fact]
    public async Task AnOnlineUrlThatIsNotHttpIsReported()
    {
        var source = new OsvVulnerabilitySource(new Options(new ScanningOptions { OsvUrl = "file:///etc" }), new OsvState());

        await Assert.ThrowsAsync<ScanUnavailableException>(
            () => source.QueryAsync(new[] { new ScanComponent("npm", "x", "1.0.0") }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AConnectionDroppedWhileTheBodyIsReadIsReportedAsAnUnavailableSource()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            var buffer = new byte[4096];
            _ = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
            var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1000\r\n\r\n{\"results\":[");
            await stream.WriteAsync(head);
            await stream.FlushAsync();
            client.Client.LingerState = new LingerOption(true, 0);
        }, TestContext.Current.CancellationToken);

        var source = new OsvVulnerabilitySource(new Options(new ScanningOptions { OsvUrl = "http://127.0.0.1:" + port }), new OsvState());

        await Assert.ThrowsAsync<ScanUnavailableException>(
            () => source.QueryAsync(new[] { new ScanComponent("npm", "x", "1.0.0") }, TestContext.Current.CancellationToken));
        await server;
    }
}
