using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>
/// The web UI of vulnerability scanning (P7): the findings section of every format's package page, <em>Scan now</em> for a
/// version, the administrators' dashboard per repository, the policy editors (repository and project, password confirmed), and the
/// scan operation. Gates: Manage on the package for scanning a version, administrators for the dashboard and policies.
/// </summary>
public class ScanningPagesTests
{
    private const int High = 2;

    private readonly ITestOutputHelper _output;

    public ScanningPagesTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private async Task<(ScanningHarness Harness, Browser Admin, FakeTrivyRunner Trivy)> StartAsync(bool trivy = false, bool scanning = true)
    {
        var fake = new FakeTrivyRunner();
        var settings = new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "Scanning:Enabled", scanning ? "true" : "false" } };
        if (trivy)
        {
            settings["Scanning:TrivyPath"] = "trivy";
            settings["Scanning:TrivyRegistryHost"] = "registry.test";
        }

        var harness = await ScanningHarness.StartAsync(_output, services =>
        {
            services.RemoveAll<ITrivyRunner>();
            services.AddSingleton<ITrivyRunner>(fake);
        }, settings);
        await CreateUserAsync(harness.App, "root", administrator: true);
        var admin = new Browser(harness.App);
        await SignInAsync(admin, "root");

        return (harness, admin, fake);
    }

    private static async Task<string> TextAsync(HttpResponseMessage response)
    {
        using (response) return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static async Task PushNuGetAsync(ScanningHarness harness, string id, string version, string repository = "")
    {
        using var package = TestPackages.Build(id, version);
        using var request = TestPackages.PushRequest(package, ScanningHarness.Key);
        request.RequestUri = new Uri(repository + "api/v2/package", UriKind.Relative);
        using var response = await harness.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task PublishNpmAsync(ScanningHarness harness, string name, string version)
    {
        var tarball = NpmTestPackages.Tarball(name, version);
        using var request = NpmTestPackages.Json(HttpMethod.Put, NpmTestPackages.Path("/r/web/npm/", name), NpmTestPackages.PublishBody(tarball), ScanningHarness.Key);
        using var response = await harness.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task DeployMavenAsync(ScanningHarness harness, string path, string content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/r/libs/maven/" + path) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("deployer:" + ScanningHarness.Key)));
        using var response = await harness.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- the findings section of the package pages ----

    [Fact]
    public async Task ANuGetPackagePageListsTheFindingsOfItsVersion()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        harness.Osv.Add("NuGet", "Acme.App", "1.0.0", "GHSA-page-nuget", "CRITICAL", "1.0.1");
        await PushNuGetAsync(harness, "Acme.App", "1.0.0");

        var before = await admin.GetPageAsync("packages/Acme.App/1.0.0");
        await harness.ScanAsync(null, "Acme.App", "1.0.0");
        var after = await admin.GetPageAsync("packages/Acme.App/1.0.0");

        Assert.Contains("data-scan-state=\"notscanned\"", before);
        Assert.Contains("data-scan-state=\"findings\"", after);
        Assert.Contains("GHSA-page-nuget", after);
        Assert.Contains("https://example.test/advisories/GHSA-page-nuget", after);
        Assert.Contains("label-danger", after);
        Assert.Contains("1.0.1", after);
        Assert.Contains("OSV.dev", after);
        Assert.Contains("id=\"scan-now-form\"", after);
    }

    [Fact]
    public async Task ACleanVersionSaysSoAndTheScanNowButtonIsForPeopleWhoManageThePackage()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        await CreateUserAsync(harness.App, "alice");
        using var alice = new Browser(harness.App);
        await SignInAsync(alice, "alice");
        await PushNuGetAsync(harness, "Acme.App", "1.0.0");
        await harness.ScanAsync(null, "Acme.App", "1.0.0");

        var asAdmin = await admin.GetPageAsync("packages/Acme.App/1.0.0");
        var asAlice = await alice.GetPageAsync("packages/Acme.App/1.0.0");
        using var anonymous = harness.App.CreateClient();
        var asAnonymous = await anonymous.GetStringAsync("packages/Acme.App/1.0.0", TestContext.Current.CancellationToken);

        Assert.Contains("id=\"scan-clean\"", asAdmin);
        Assert.Contains("data-scan-state=\"clean\"", asAnonymous);
        Assert.Contains("id=\"scan-now-form\"", asAdmin);
        // Alice is signed in but owns nothing: ownership (nobody owns the package) settles Manage, and a reader cannot scan.
        Assert.DoesNotContain("id=\"scan-now-form\"", asAnonymous);
        Assert.Contains("id=\"scan-clean\"", asAlice);
    }

    [Fact]
    public async Task APolicyThatBlocksDownloadsIsExplainedOnThePage()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        await harness.App.CreateNpmRepositoryAsync("web", cancellationToken: TestContext.Current.CancellationToken);
        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-page-npm", "HIGH");
        await PublishNpmAsync(harness, "lib", "1.0.0");
        await harness.ScanAsync("web", "lib", "1.0.0");
        await harness.SetPolicyAsync("web", blockDownload: High);

        var html = await admin.GetPageAsync("r/web/npm-packages/lib?version=1.0.0");

        Assert.Contains("id=\"scan-blocked\"", html);
        Assert.Contains("GHSA-page-npm", html);
        Assert.Contains("Downloads of this version are refused", html);
    }

    [Fact]
    public async Task AWarnPolicyShowsAWarning()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        await harness.App.CreateNpmRepositoryAsync("web", cancellationToken: TestContext.Current.CancellationToken);
        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-warn-page", "MODERATE");
        await PublishNpmAsync(harness, "lib", "1.0.0");
        await harness.ScanAsync("web", "lib", "1.0.0");
        await harness.SetPolicyAsync("web", warn: 0);

        var html = await admin.GetPageAsync("r/web/npm-packages/lib?version=1.0.0");

        Assert.Contains("id=\"scan-warning\"", html);
        Assert.DoesNotContain("id=\"scan-blocked\"", html);
    }

    [Fact]
    public async Task AMavenArtifactPageShowsItsFindings()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        await harness.App.CreateMavenRepositoryAsync("libs", cancellationToken: TestContext.Current.CancellationToken);
        harness.Osv.Add("Maven", "org.acme:lib", "1.0", "GHSA-page-maven", "HIGH");
        await DeployMavenAsync(harness, "org/acme/lib/1.0/lib-1.0.pom", "<project><modelVersion>4.0.0</modelVersion><groupId>org.acme</groupId><artifactId>lib</artifactId><version>1.0</version></project>");
        await harness.ScanAsync("libs", "org.acme:lib", "1.0");

        var html = await admin.GetPageAsync("r/libs/maven-artifacts/org.acme/lib/1.0");

        Assert.Contains("data-scan-state=\"findings\"", html);
        Assert.Contains("GHSA-page-maven", html);
    }

    [Fact]
    public async Task ADockerManifestPageSaysImagesAreUnscannedWithoutTrivyAndShowsFindingsWithIt()
    {
        var (without, adminWithout, _) = await StartAsync(trivy: false);
        await using (without)
        using (adminWithout)
        {
            await without.App.CreateDockerRepositoryAsync("libs", cancellationToken: TestContext.Current.CancellationToken);
            var image = OciTestImage.Create("page");
            await PushImageAsync(without, image);

            var html = await adminWithout.GetPageAsync($"r/libs/docker-images/app?digest={Uri.EscapeDataString(image.ManifestDigest)}");

            Assert.Contains("data-scan-state=\"unavailable\"", html);
            Assert.Contains("Trivy is not configured", html);
        }

        var (with, adminWith, trivy) = await StartAsync(trivy: true);
        await using (with)
        using (adminWith)
        {
            await with.App.CreateDockerRepositoryAsync("libs", cancellationToken: TestContext.Current.CancellationToken);
            trivy.Hits.Add(new ScanHit("CVE-2024-9999", "openssl", "1.1.1", 3, "Heap overflow", "https://avd.example.test/CVE-2024-9999", "1.1.2"));
            var image = OciTestImage.Create("page");
            await PushImageAsync(with, image);
            await with.ScanAsync("libs", "app", image.ManifestDigest);

            var html = await adminWith.GetPageAsync($"r/libs/docker-images/app?digest={Uri.EscapeDataString(image.ManifestDigest)}");

            Assert.Contains("data-scan-state=\"findings\"", html);
            Assert.Contains("CVE-2024-9999", html);
            Assert.Contains("Trivy", html);
        }
    }

    private static async Task PushImageAsync(ScanningHarness harness, OciTestImage image)
    {
        string? upload = null;
        async Task SendAsync(HttpMethod method, string path, byte[]? body = null, string? contentType = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body != null)
            {
                request.Content = new ByteArrayContent(body);
                if (contentType != null) request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("ci:" + ScanningHarness.Key)));
            using var response = await harness.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, $"{method} {path}: {response.StatusCode}");
            if (response.StatusCode == HttpStatusCode.Accepted && response.Headers.Location != null && body == null)
            {
                upload = response.Headers.Location.ToString();
            }
        }

        foreach (var blob in new[] { image.Config }.Concat(image.Layers))
        {
            await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/");
            await SendAsync(HttpMethod.Put, upload + "?digest=" + Uri.EscapeDataString(OciTestImage.Digest(blob)), blob, "application/octet-stream");
        }

        await SendAsync(HttpMethod.Put, "/v2/libs/app/manifests/latest", image.Manifest, OciTestImage.ManifestType);
    }

    [Fact]
    public async Task WithScanningOffThePagesShowNoSection()
    {
        var (harness, admin, _) = await StartAsync(scanning: false);
        await using var _h = harness;
        using var _a = admin;
        await PushNuGetAsync(harness, "Acme.App", "1.0.0");

        var html = await admin.GetPageAsync("packages/Acme.App/1.0.0");

        Assert.DoesNotContain("id=\"scan-findings\"", html);
    }

    // ---- scan now ----

    [Fact]
    public async Task ScanNowScansTheVersionAndShowsTheOutcome()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        harness.Osv.Add("NuGet", "Acme.App", "1.0.0", "GHSA-now", "HIGH");
        await PushNuGetAsync(harness, "Acme.App", "1.0.0");

        using var response = await admin.PostAsync(
            "scan-artifact?name=Acme.App&version=1.0.0",
            new Dictionary<string, string> { { "Name", "Acme.App" }, { "Version", "1.0.0" }, { "ReturnUrl", "/packages/Acme.App/1.0.0" } });
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("1 finding(s)", html);
        Assert.Contains("GHSA-now", html);
        Assert.Equal(1, (await harness.ResultAsync(null, "Acme.App", "1.0.0"))!.FindingCount);
    }

    [Fact]
    public async Task ScanNowIsRefusedToAnonymousVisitorsAndToPeopleWhoCannotManageThePackage()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        await CreateUserAsync(harness.App, "alice");
        using var alice = new Browser(harness.App);
        await SignInAsync(alice, "alice");
        await PushNuGetAsync(harness, "Acme.App", "1.0.0");
        // Alice owns another package; Acme.App is owned by root, so she may not manage it.
        await harness.RunAsync(null, async sp =>
        {
            await sp.GetRequiredService<PackageOwnershipService>().AddOwnerAsync("Acme.App", "root", TestContext.Current.CancellationToken);

            return 0;
        });

        using var forAlice = await alice.PostAsync("scan-artifact", new Dictionary<string, string> { { "Name", "Acme.App" }, { "Version", "1.0.0" } }, tokenPage: BrowserUpload.TokenPage);
        using var anonymous = harness.App.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var forAnonymous = await anonymous.GetAsync("scan-artifact?name=Acme.App&version=1.0.0", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, forAlice.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, forAnonymous.StatusCode);
        Assert.Null(await harness.ResultAsync(null, "Acme.App", "1.0.0"));
    }

    [Fact]
    public async Task ScanNowOfAnImageQueuesTheScanForTheBackgroundWorker()
    {
        var (harness, admin, trivy) = await StartAsync(trivy: true);
        await using var _h = harness;
        using var _a = admin;
        await harness.App.CreateDockerRepositoryAsync("libs", cancellationToken: TestContext.Current.CancellationToken);
        trivy.Hits.Add(new ScanHit("CVE-2024-1111", "zlib", "1.2.0", 1, null, "https://avd.example.test/CVE-2024-1111", null));
        var image = OciTestImage.Create("queue");
        await PushImageAsync(harness, image);

        using var response = await admin.PostAsync(
            "r/libs/scan-artifact?name=app&version=" + Uri.EscapeDataString(image.ManifestDigest),
            new Dictionary<string, string> { { "Name", "app" }, { "Version", image.ManifestDigest } });
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("queued for a scan with Trivy", html);
        ScanResult? result = null;
        for (var i = 0; i < 100 && result?.ScannedAt == null; i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            result = await harness.ResultAsync("libs", "app", image.ManifestDigest);
        }

        Assert.Equal(1, result?.FindingCount);
    }

    // ---- the administrators' pages ----

    [Fact]
    public async Task TheScanningPagesAreForAdministratorsOnly()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        await CreateUserAsync(harness.App, "alice");
        using var alice = new Browser(harness.App);
        await SignInAsync(alice, "alice");

        foreach (var page in new[] { "admin/scanning", "admin/scanning/default" })
        {
            using var forAlice = await alice.GetAsync(page);
            using var forAdmin = await admin.GetAsync(page);
            Assert.Equal(HttpStatusCode.NotFound, forAlice.StatusCode);
            Assert.Equal(HttpStatusCode.OK, forAdmin.StatusCode);
        }
    }

    [Fact]
    public async Task TheDashboardCountsArtifactsBySeverityAndListsTheVersionsWithFindings()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        harness.Osv.Add("NuGet", "Bad.One", "1.0.0", "GHSA-dash-1", "CRITICAL");
        harness.Osv.Add("NuGet", "Bad.Two", "1.0.0", "GHSA-dash-2", "LOW");
        foreach (var id in new[] { "Bad.One", "Bad.Two", "Good.One", "Never.Scanned" }) await PushNuGetAsync(harness, id, "1.0.0");
        foreach (var id in new[] { "Bad.One", "Bad.Two", "Good.One" }) await harness.ScanAsync(null, id, "1.0.0");

        var overview = await admin.GetPageAsync("admin/scanning");
        var html = await admin.GetPageAsync("admin/scanning/default");
        var critical = await admin.GetPageAsync("admin/scanning/default?min=3");

        Assert.Contains("data-repository=\"default\"", overview);
        Assert.Contains("id=\"scan-count-artifacts\">4<", html);
        Assert.Contains("id=\"scan-count-critical\">1<", html);
        Assert.Contains("id=\"scan-count-low\">1<", html);
        Assert.Contains("id=\"scan-count-clean\">1<", html);
        Assert.Contains("id=\"scan-count-unscanned\">1<", html);
        Assert.Contains("data-artifact=\"bad.one\"", html);
        Assert.Contains("data-artifact=\"bad.two\"", html);
        Assert.Contains("data-artifact=\"bad.one\"", critical);
        Assert.DoesNotContain("data-artifact=\"bad.two\"", critical);
        Assert.Contains("/packages/bad.one/1.0.0", html);
    }

    [Fact]
    public async Task ThePolicyIsSavedWithThePasswordAndEnforcedAndEmptyValuesRemoveIt()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        harness.Osv.Add("NuGet", "Acme.App", "1.0.0", "GHSA-policy", "HIGH");
        await PushNuGetAsync(harness, "Acme.App", "1.0.0");
        await harness.ScanAsync(null, "Acme.App", "1.0.0");
        var form = new Dictionary<string, string> { { "Warn", "1" }, { "BlockDownload", "2" }, { "BlockDeploy", "" }, { "BlockUnscanned", "true" }, { "password", Password } };

        using var wrong = await admin.PostAsync("admin/scanning/default", new Dictionary<string, string>(form) { ["password"] = "nope" }, "admin/scanning/default?handler=Policy");
        var wrongPage = await admin.GetPageAsync("admin/scanning/default");
        Assert.Contains("The confirmation is incorrect", wrongPage);
        using var download = await harness.Client.GetAsync("v3/package/acme.app/1.0.0/acme.app.1.0.0.nupkg", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);

        using var saved = await admin.PostAsync("admin/scanning/default", form, "admin/scanning/default?handler=Policy");
        var page = await admin.GetPageAsync("admin/scanning/default");
        using var blocked = await harness.Client.GetAsync("v3/package/acme.app/1.0.0/acme.app.1.0.0.nupkg", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("has its own policy", page);
        Assert.Contains("<option value=\"2\" selected=\"selected\">", page);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        using var removed = await admin.PostAsync("admin/scanning/default", new Dictionary<string, string> { { "Warn", "" }, { "BlockDownload", "" }, { "BlockDeploy", "" }, { "BlockUnscanned", "false" }, { "password", Password } }, "admin/scanning/default?handler=Policy");
        using var served = await harness.Client.GetAsync("v3/package/acme.app/1.0.0/acme.app.1.0.0.nupkg", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Contains("No policy applies", await admin.GetPageAsync("admin/scanning/default"));
    }

    [Fact]
    public async Task APolicyOfAProjectIsEditedOnItsOwnPageAndReachesItsRepositories()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        await harness.RunAsync(null, async sp =>
        {
            var administration = sp.GetRequiredService<IAccessAdministration>();
            await administration.CreateProjectAsync("team", "Team", "root", TestContext.Current.CancellationToken);
            await administration.SetRepositoryProjectAsync("default", "team", "root", TestContext.Current.CancellationToken);

            return 0;
        });

        var detail = await admin.GetPageAsync("admin/projects/team");
        using var saved = await admin.PostAsync(
            "admin/scanning/projects/team",
            new Dictionary<string, string> { { "Warn", "" }, { "BlockDownload", "3" }, { "BlockDeploy", "" }, { "BlockUnscanned", "false" }, { "password", Password } },
            "admin/scanning/projects/team?handler=Policy");
        var repository = await admin.GetPageAsync("admin/scanning/default");

        Assert.Contains("id=\"project-scanning-link\"", detail);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("The policy of the project", repository);
        Assert.Contains("team", repository);
        Assert.Contains("(policy set)", await admin.GetPageAsync("admin/scanning"));
    }

    [Fact]
    public async Task ScanTheWholeRepositoryRunsAsAnOperationWhoseStatusIsShown()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        harness.Osv.Add("NuGet", "Acme.App", "1.0.0", "GHSA-job", "HIGH");
        await PushNuGetAsync(harness, "Acme.App", "1.0.0");

        using var started = await admin.PostAsync("admin/scanning/default", new Dictionary<string, string>(), "admin/scanning/default?handler=Scan");
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);

        string html = string.Empty;
        for (var i = 0; i < 100; i++)
        {
            html = await admin.GetPageAsync("admin/scanning/default");
            if (html.Contains("Scanned 1 artifact(s)")) break;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Contains("Scanned 1 artifact(s): 1 with findings", html);
        Assert.Contains("data-artifact=\"acme.app\"", html);
        Assert.Contains("vulnerability-scan", await admin.GetPageAsync("admin/operations"));
    }

    [Fact]
    public async Task APolicyChangeIsRefusedWhileTheServerIsReadOnly()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        harness.App.Services.GetRequiredService<MaintenanceGate>().Close("copy");

        using var response = await admin.PostAsync(
            "admin/scanning/default",
            new Dictionary<string, string> { { "BlockDownload", "2" }, { "password", Password } },
            "admin/scanning/default?handler=Policy");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task DeletingARepositoryDropsItsScanResultsAndPolicy()
    {
        var (harness, admin, _) = await StartAsync();
        await using var _h = harness;
        using var _a = admin;
        await harness.App.CreateRepositoryAsync("temp", TestContext.Current.CancellationToken);
        await harness.SetPolicyAsync("temp", blockDownload: High);
        await harness.RunAsync("temp", async sp =>
        {
            var context = sp.GetRequiredService<IContext>();
            var result = new ScanResult { Name = "x", Version = "1.0.0", Scanner = "osv", Attempted = DateTime.UtcNow, ScannedAt = DateTime.UtcNow, Findings = new List<ScanFinding> { new() { VulnerabilityId = "GHSA-x", Component = "x", ComponentVersion = "1.0.0", Severity = 1 } } };
            context.ScanResults!.Add(result);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return 0;
        });

        await harness.RunAsync(null, async sp =>
        {
            await sp.GetRequiredService<IRepositoryAdministration>().DeleteAsync("temp", TestContext.Current.CancellationToken);

            return 0;
        });

        var counts = await harness.RunAsync(null, async sp =>
        {
            var context = sp.GetRequiredService<IContext>();
            return (
                Results: context.ScanResults!.IgnoreQueryFilters().Count(),
                Findings: context.ScanFindings!.Count(),
                Policies: context.ScanPolicies!.Count());
        });
        Assert.Equal((0, 0, 0), counts);
    }
}
