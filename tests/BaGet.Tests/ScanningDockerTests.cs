using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Vulnerability scanning of Docker repositories (P7): images are scanned by Trivy (a fake stands in for the executable), the
/// policy blocks a pull with <c>403 DENIED</c> and removes a pushed image that reaches the deploy threshold, and without Trivy images
/// are marked unscanned.
/// </summary>
public class ScanningDockerTests
{
    private const int Low = 0, High = 2, Critical = 3;

    private readonly ITestOutputHelper _output;

    public ScanningDockerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static ScanHit Hit(string id, int severity, string component = "openssl", string version = "1.1.1") =>
        new(id, component, version, severity, "A flaw in " + component, "https://avd.example.test/" + id, "1.1.2");

    private async Task<(ScanningHarness Harness, FakeTrivyRunner Trivy)> StartAsync(bool configured = true)
    {
        var trivy = new FakeTrivyRunner();
        var settings = configured
            ? new Dictionary<string, string> { { "Scanning:TrivyPath", "trivy" }, { "Scanning:TrivyRegistryHost", "registry.test" } }
            : new Dictionary<string, string>();
        var harness = await ScanningHarness.StartAsync(
            _output,
            services =>
            {
                services.RemoveAll<ITrivyRunner>();
                services.AddSingleton<ITrivyRunner>(trivy);
            },
            settings);
        await harness.App.CreateDockerRepositoryAsync("libs", cancellationToken: TestContext.Current.CancellationToken);

        return (harness, trivy);
    }

    private static AuthenticationHeaderValue Basic() => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("ci:" + ScanningHarness.Key)));

    private static async Task<HttpResponseMessage> SendAsync(ScanningHarness harness, HttpMethod method, string path, byte[]? body = null, string? contentType = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
        {
            request.Content = new ByteArrayContent(body);
            if (contentType != null) request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        request.Headers.Authorization = Basic();

        return await harness.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task PushBlobAsync(ScanningHarness harness, string image, byte[] content)
    {
        using var start = await SendAsync(harness, HttpMethod.Post, $"/v2/{image}/blobs/uploads/");
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var finish = await SendAsync(harness, HttpMethod.Put, start.Headers.Location!.ToString() + "?digest=" + Uri.EscapeDataString(OciTestImage.Digest(content)), content, "application/octet-stream");
        Assert.Equal(HttpStatusCode.Created, finish.StatusCode);
    }

    private static async Task<HttpResponseMessage> PushImageAsync(ScanningHarness harness, string image, string tag, OciTestImage content)
    {
        await PushBlobAsync(harness, image, content.Config);
        foreach (var layer in content.Layers) await PushBlobAsync(harness, image, layer);

        return await SendAsync(harness, HttpMethod.Put, $"/v2/{image}/manifests/{tag}", content.Manifest, OciTestImage.ManifestType);
    }

    private static async Task<string> TextAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task AnImageIsScannedByTrivyUnderItsRegistryNameAndDigest()
    {
        var (harness, trivy) = await StartAsync();
        await using var _ = harness;
        trivy.Hits.Add(Hit("CVE-2024-0001", Critical));
        trivy.Hits.Add(Hit("CVE-2024-0002", Low, "zlib", "1.2.0"));
        var image = OciTestImage.Create("one");
        (await PushImageAsync(harness, "libs/app", "latest", image)).Dispose();

        var result = (await harness.ScanAsync("libs", "app", image.ManifestDigest))!;

        Assert.Equal("trivy", result.Scanner);
        Assert.Equal(Critical, result.Highest);
        Assert.Equal(2, result.FindingCount);
        Assert.Equal(new[] { "registry.test/libs/app@" + image.ManifestDigest }, trivy.References);
        var found = (await harness.ResultAsync("libs", "app", image.ManifestDigest))!;
        Assert.Equal("openssl", found.Findings!.First().Component);
        Assert.Equal("1.1.2", found.Findings!.First().FixedVersion);
        Assert.Equal("https://avd.example.test/CVE-2024-0001", found.Findings!.First().AdvisoryUrl);
    }

    [Fact]
    public async Task ABlockDownloadPolicyRefusesAManifestPullWith403DeniedAndTheReason()
    {
        var (harness, trivy) = await StartAsync();
        await using var _ = harness;
        trivy.Hits.Add(Hit("CVE-2024-0003", Critical));
        var vulnerable = OciTestImage.Create("vulnerable");
        (await PushImageAsync(harness, "libs/app", "latest", vulnerable)).Dispose();
        await harness.ScanAsync("libs", "app", vulnerable.ManifestDigest);
        await harness.SetPolicyAsync("libs", blockDownload: High);

        using var byTag = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/latest");
        using var byDigest = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/" + vulnerable.ManifestDigest);
        using var head = await SendAsync(harness, HttpMethod.Head, "/v2/libs/app/manifests/latest");

        foreach (var response in new[] { byTag, byDigest, head }) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var error = JsonDocument.Parse(await TextAsync(byTag));
        var entry = error.RootElement.GetProperty("errors")[0];
        Assert.Equal("DENIED", entry.GetProperty("code").GetString());
        Assert.Contains("CVE-2024-0003", entry.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ABlockDownloadPolicyOfAPrivateMemberStillRefusesAPullThroughAVirtualRepositoryWithARegistryToken()
    {
        var (harness, trivy) = await StartAsync();
        await using var _ = harness;
        await harness.App.CreateDockerRepositoryAsync("secret", anonymousRead: false, cancellationToken: TestContext.Current.CancellationToken);
        using (var scope = harness.App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().CreateAsync(
                "all",
                new VirtualRepositorySettings { Members = new List<string> { "secret" } },
                TestContext.Current.CancellationToken,
                actor: null,
                anonymousRead: true,
                RepositoryFormat.Docker);
        }

        trivy.Hits.Add(Hit("CVE-2024-0009", Critical));
        var vulnerable = OciTestImage.Create("hidden");
        (await PushImageAsync(harness, "secret/app", "latest", vulnerable)).Dispose();
        await harness.ScanAsync("secret", "app", vulnerable.ManifestDigest);
        await harness.SetPolicyAsync("secret", blockDownload: High);

        // docker login with the key: the token (not the key) reaches the private member through the virtual repository.
        using var tokenResponse = await SendAsync(harness, HttpMethod.Get, "/v2/token?service=BaGet&scope=" + Uri.EscapeDataString("repository:all/app:pull"));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var json = JsonDocument.Parse(await TextAsync(tokenResponse));
        using var pull = new HttpRequestMessage(HttpMethod.Get, "/v2/all/app/manifests/latest");
        pull.Headers.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());

        using var response = await harness.Client.SendAsync(pull, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnImageWithoutFindingsAboveTheThresholdIsPulledAsUsual()
    {
        var (harness, trivy) = await StartAsync();
        await using var _ = harness;
        trivy.Hits.Add(Hit("CVE-2024-0004", Low));
        var image = OciTestImage.Create("fine");
        (await PushImageAsync(harness, "libs/app", "latest", image)).Dispose();
        await harness.ScanAsync("libs", "app", image.ManifestDigest);
        await harness.SetPolicyAsync("libs", blockDownload: High);

        using var response = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/latest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ABlockDeployPolicyScansAPushedImageAndRemovesItWhenItReachesTheThreshold()
    {
        var (harness, trivy) = await StartAsync();
        await using var _ = harness;
        trivy.Hits.Add(Hit("CVE-2024-0005", Critical));
        await harness.SetPolicyAsync("libs", blockDeploy: High);
        var image = OciTestImage.Create("rejected");

        using var put = await PushImageAsync(harness, "libs/app", "latest", image);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Contains("CVE-2024-0005", await TextAsync(put));
        // Pushed and judged straight away: nothing of it is left to pull.
        await harness.SetPolicyAsync("libs");
        using var pull = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/latest");
        Assert.Equal(HttpStatusCode.NotFound, pull.StatusCode);

        trivy.Hits.Clear();
        trivy.Hits.Add(Hit("CVE-2024-0006", Low));
        await harness.SetPolicyAsync("libs", blockDeploy: High);
        var fine = OciTestImage.Create("accepted");
        using var accepted = await PushImageAsync(harness, "libs/app", "latest", fine);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task WithoutTrivyImagesAreUnscannedAndNothingIsBlocked()
    {
        var (harness, trivy) = await StartAsync(configured: false);
        await using var _ = harness;
        var image = OciTestImage.Create("unscanned");
        await harness.SetPolicyAsync("libs", blockDownload: Low, blockDeploy: Low);
        using var put = await PushImageAsync(harness, "libs/app", "latest", image);

        var status = await harness.RunAsync("libs", sp => Task.FromResult(sp.GetRequiredService<IScanService>().GetEngineStatus()));
        var summary = await harness.RunAsync("libs", sp => sp.GetRequiredService<IScanService>().SummarizeAsync(TestContext.Current.CancellationToken));
        var scan = await Assert.ThrowsAsync<ScanUnavailableException>(() => harness.ScanAsync("libs", "app", image.ManifestDigest));
        using var pull = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/latest");

        Assert.Equal(HttpStatusCode.Created, put.StatusCode);
        Assert.False(status.Available);
        Assert.Contains("Trivy is not configured", status.Reason);
        Assert.Contains("Trivy is not configured", scan.Message);
        Assert.Equal(1, summary.Artifacts);
        Assert.Equal(1, summary.Unscanned);
        Assert.Equal(HttpStatusCode.OK, pull.StatusCode);
        Assert.Empty(trivy.References);
    }

    [Fact]
    public async Task ATrivyThatFailsLeavesAFailedResultAndTheImagePullable()
    {
        var (harness, trivy) = await StartAsync();
        await using var _ = harness;
        trivy.Failure = "Trivy failed (exit code 1): database unavailable";
        var image = OciTestImage.Create("failing");
        (await PushImageAsync(harness, "libs/app", "latest", image)).Dispose();
        await harness.SetPolicyAsync("libs", blockDownload: Low);

        var result = (await harness.ScanAsync("libs", "app", image.ManifestDigest))!;
        using var pull = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/latest");

        Assert.Null(result.ScannedAt);
        Assert.Contains("database unavailable", result.Error);
        Assert.Equal(HttpStatusCode.OK, pull.StatusCode);
    }

    [Fact]
    public async Task OnlyImageManifestsAreScannedNotIndexesOrArtifactsWithAForeignConfig()
    {
        var (harness, trivy) = await StartAsync();
        await using var _ = harness;
        var image = OciTestImage.Create("platform");
        (await PushImageAsync(harness, "libs/app", "latest", image)).Dispose();
        // artifactType and subject are self-declared and ignored by runtimes: such a manifest with an image config is still an image.
        var disguised = OciTestImage.Create("disguised", subject: image.ManifestDigest, artifactType: "application/x-foo");
        (await PushImageAsync(harness, "libs/app", "disguised", disguised)).Dispose();

        var artifacts = await harness.RunAsync("libs", sp => sp.GetFormatService<IFormatScanner>(RepositoryFormat.Docker)!.ListArtifactsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new[] { image.ManifestDigest, disguised.ManifestDigest }.OrderBy(d => d), artifacts.Select(a => a.Version).OrderBy(d => d));
        const string Config = "{\"config\":{\"mediaType\":\"{0}\"}}";
        Assert.True(DockerFormatScanner.IsImage(OciTestImage.ManifestType, Config.Replace("{0}", OciTestImage.ConfigType)));
        Assert.False(DockerFormatScanner.IsImage(OciTestImage.IndexType, "{\"manifests\":[]}"));
        Assert.False(DockerFormatScanner.IsImage(OciTestImage.ManifestType, Config.Replace("{0}", "application/vnd.dev.cosign.artifact.sig.v1+json")));
        Assert.False(DockerFormatScanner.IsImage(OciTestImage.ManifestType, "not json"));
    }

    [Fact]
    public async Task BlockUnscannedRefusesNothingWhileNoEngineCanScan()
    {
        var (harness, _) = await StartAsync(configured: false);
        await using var _ = harness;
        (await PushImageAsync(harness, "libs/app", "latest", OciTestImage.Create("no-engine"))).Dispose();
        await harness.SetPolicyAsync("libs", blockUnscanned: true);

        using var pull = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/latest");

        Assert.Equal(HttpStatusCode.OK, pull.StatusCode);
    }

    [Fact]
    public async Task BlockUnscannedLeavesSignaturesAndOtherArtifactsPullable()
    {
        var (harness, _) = await StartAsync();
        await using var _ = harness;
        var image = OciTestImage.Create("signed");
        (await PushImageAsync(harness, "libs/app", "latest", image)).Dispose();
        var signature = OciTestImage.Create("sig", subject: image.ManifestDigest, artifactType: "application/vnd.dev.cosign.artifact.sig.v1+json", configType: "application/vnd.dev.cosign.artifact.sig.v1+json");
        (await PushImageAsync(harness, "libs/app", "sig", signature)).Dispose();
        await harness.SetPolicyAsync("libs", blockUnscanned: true);

        using var pull = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/" + signature.ManifestDigest);

        Assert.Equal(HttpStatusCode.OK, pull.StatusCode);
    }

    [Fact]
    public async Task AnImageThatDeclaresAnArtifactTypeAndASubjectIsStillJudgedOnPushAndOnPull()
    {
        var (harness, trivy) = await StartAsync();
        await using var _ = harness;
        var anchor = OciTestImage.Create("anchor");
        (await PushImageAsync(harness, "libs/app", "latest", anchor)).Dispose();
        var disguised = OciTestImage.Create("disguised-pull", subject: anchor.ManifestDigest, artifactType: "application/x-foo");
        (await PushImageAsync(harness, "libs/app", "disguised", disguised)).Dispose();
        await harness.SetPolicyAsync("libs", blockUnscanned: true);

        using var pull = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/" + disguised.ManifestDigest);

        Assert.Equal(HttpStatusCode.Forbidden, pull.StatusCode);

        trivy.Hits.Add(Hit("CVE-2024-0011", Critical));
        await harness.SetPolicyAsync("libs", blockDeploy: High);
        var bad = OciTestImage.Create("disguised-push", subject: anchor.ManifestDigest, artifactType: "application/x-foo");

        using var put = await PushImageAsync(harness, "libs/app", "bad", bad);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    [Fact]
    public async Task TheScannersOwnPullIsNotRefusedAsUnscanned()
    {
        var (harness, trivy) = await StartAsync();
        await using var _ = harness;
        var image = OciTestImage.Create("to-scan");
        (await PushImageAsync(harness, "libs/app", "latest", image)).Dispose();
        await harness.SetPolicyAsync("libs", blockUnscanned: true);
        HttpStatusCode? during = null;
        trivy.OnScan = async _ =>
        {
            using var pull = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/" + image.ManifestDigest);
            during = pull.StatusCode;
        };

        var result = (await harness.ScanAsync("libs", "app", image.ManifestDigest))!;
        trivy.OnScan = null;
        using var after = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/latest");

        Assert.Equal(HttpStatusCode.OK, during);
        Assert.NotNull(result.ScannedAt);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task ARescanOfAnImageWithFindingsDoesNotLiftTheDownloadSeverityBlockForOtherClients()
    {
        var (harness, trivy) = await StartAsync();
        await using var _ = harness;
        trivy.Hits.Add(Hit("CVE-2024-0009", Critical));
        var image = OciTestImage.Create("rescanned");
        (await PushImageAsync(harness, "libs/app", "latest", image)).Dispose();
        await harness.ScanAsync("libs", "app", image.ManifestDigest);
        await harness.SetPolicyAsync("libs", blockDownload: High);
        HttpStatusCode? during = null;
        trivy.OnScan = async _ =>
        {
            using var pull = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/" + image.ManifestDigest);
            during = pull.StatusCode;
        };

        await harness.ScanAsync("libs", "app", image.ManifestDigest);
        trivy.OnScan = null;

        Assert.Equal(HttpStatusCode.Forbidden, during);
    }

    [Fact]
    public async Task APushRefusedByBlockDeployPutsAMovedTagBackEvenForAManifestTheImageAlreadyHeld()
    {
        var (harness, trivy) = await StartAsync();
        await using var _ = harness;
        var good = OciTestImage.Create("good");
        var bad = OciTestImage.Create("bad");
        (await PushImageAsync(harness, "libs/app", "prod", good)).Dispose();
        (await PushImageAsync(harness, "libs/app", "other", bad)).Dispose();
        trivy.Hits.Add(Hit("CVE-2024-0007", Critical));
        await harness.SetPolicyAsync("libs", blockDeploy: High);

        using var put = await PushImageAsync(harness, "libs/app", "prod", bad);
        using var prod = await SendAsync(harness, HttpMethod.Head, "/v2/libs/app/manifests/prod");

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(good.ManifestDigest, prod.Headers.GetValues("Docker-Content-Digest").Single());

        using var fresh = await PushImageAsync(harness, "libs/app", "brand-new", bad);
        using var gone = await SendAsync(harness, HttpMethod.Get, "/v2/libs/app/manifests/brand-new");
        Assert.Equal(HttpStatusCode.Forbidden, fresh.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }
}
