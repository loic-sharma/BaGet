using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using Xunit;
using BaGet.Validation.Controllers.Protocols;
using BaGet.Validation.Pipeline;

namespace BaGet.Tests;

/// <summary>
/// With validation on, a push is accepted, staged and validated, and the package only
/// appears once every validator passes.
/// </summary>
public class ValidationIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public ValidationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task APackageBeingValidatedIsNotServed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var gate = new GateValidator();
        var client = CreateClient(configureServices: s => s.AddSingleton<IPackageValidator>(gate));

        Assert.Equal(HttpStatusCode.Accepted, await PushAsync(client, "Gated.Package", "1.0.0"));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        Assert.Equal("Validating", await StatusAsync(client, "Gated.Package", "1.0.0"));
        await AssertNotServedAsync(client, "Gated.Package", "1.0.0");

        gate.Release.SetResult(true);

        Assert.Equal("Available", await WaitForResultAsync(client, "Gated.Package", "1.0.0"));
        using var download = await client.GetAsync("v3/package/gated.package/1.0.0/gated.package.1.0.0.nupkg", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task AFailedPackageIsQuarantinedAndNeverServed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = CreateClient(configureServices: s => s.AddSingleton<IPackageValidator>(new RejectingValidator()));

        Assert.Equal(HttpStatusCode.Accepted, await PushAsync(client, "Rejected.Package", "1.0.0"));

        Assert.Equal("Quarantined", await WaitForResultAsync(client, "Rejected.Package", "1.0.0"));
        await AssertNotServedAsync(client, "Rejected.Package", "1.0.0");

        using var status = JsonDocument.Parse(await client.GetStringAsync("v3/package/Rejected.Package/1.0.0/validation", cancellationToken));
        var rejection = status.RootElement.GetProperty("steps").EnumerateArray()
            .Single(s => s.GetProperty("type").GetString() == "Rejecting");
        Assert.Equal("Failed", rejection.GetProperty("state").GetString());
        Assert.Equal("Rejected by the test.", rejection.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AnUnsignedPackageIsQuarantinedWhenSignaturesAreRequired()
    {
        var client = CreateClient(new Dictionary<string, string> { { "Validation:RequireSignedPackages", "true" } });

        await PushAsync(client, "Unsigned.Package", "1.0.0");

        Assert.Equal("Quarantined", await WaitForResultAsync(client, "Unsigned.Package", "1.0.0"));
    }

    [Fact]
    public async Task TheSameVersionCannotBeValidatedTwiceAtOnce()
    {
        var gate = new GateValidator();
        var client = CreateClient(configureServices: s => s.AddSingleton<IPackageValidator>(gate));

        Assert.Equal(HttpStatusCode.Accepted, await PushAsync(client, "Twice.Package", "1.0.0"));
        Assert.Equal(HttpStatusCode.Conflict, await PushAsync(client, "Twice.Package", "1.0.0"));

        gate.Release.SetResult(true);
        Assert.Equal("Available", await WaitForResultAsync(client, "Twice.Package", "1.0.0"));
    }

    [Fact]
    public async Task AValidatedPackageIsRepositorySigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (path, fingerprint) = CreateSigningCertificate();
        var client = CreateClient(new Dictionary<string, string>
        {
            { "Validation:RepositorySigningCertificatePath", path },
            { "Validation:RepositorySigningCertificatePassword", "test" },
            { "Validation:RepositorySigningServiceIndexUrl", "https://localhost/v3/index.json" },
        });

        await PushAsync(client, "Signed.Package", "1.0.0");
        Assert.Equal("Available", await WaitForResultAsync(client, "Signed.Package", "1.0.0"));

        using var download = await client.GetAsync("v3/package/signed.package/1.0.0/signed.package.1.0.0.nupkg", cancellationToken);
        using var package = new MemoryStream(await download.Content.ReadAsByteArrayAsync(cancellationToken));
        using var reader = new PackageArchiveReader(package);

        Assert.True(await reader.IsSignedAsync(CancellationToken.None));
        var signature = await reader.GetPrimarySignatureAsync(CancellationToken.None);
        #pragma warning disable CS8602
        Assert.Equal(SignatureType.Repository, signature.Type);
        #pragma warning restore CS8602

        // The resource lists the certificate that signed it, and serves it.
        using var resource = JsonDocument.Parse(await client.GetStringAsync("v3/repository-signatures/index.json", cancellationToken));
        var certificate = resource.RootElement.GetProperty("signingCertificates")[0];
        Assert.Equal(fingerprint, certificate.GetProperty("fingerprints").GetProperty("2.16.840.1.101.3.4.2.1").GetString());
        Assert.False(resource.RootElement.GetProperty("allRepositorySigned").GetBoolean());

        using var crt = await client.GetAsync(certificate.GetProperty("contentUrl").GetString(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, crt.StatusCode);

        var index = await client.GetStringAsync("v3/index.json", cancellationToken);
        Assert.Contains("RepositorySignatures/5.0.0", index);
    }

    [Fact]
    public async Task ASignedPackagePassesWhenSignaturesAreRequired()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Sign a package on one server, then push the signed copy to another that
        // requires signatures: its signature must verify, untrusted root and all.
        var (path, _) = CreateSigningCertificate();
        var signer = CreateClient(new Dictionary<string, string>
        {
            { "Validation:RepositorySigningCertificatePath", path },
            { "Validation:RepositorySigningCertificatePassword", "test" },
            { "Validation:RepositorySigningServiceIndexUrl", "https://localhost/v3/index.json" },
        });
        await PushAsync(signer, "Resigned.Package", "1.0.0");
        Assert.Equal("Available", await WaitForResultAsync(signer, "Resigned.Package", "1.0.0"));
        var signed = await signer.GetByteArrayAsync("v3/package/resigned.package/1.0.0/resigned.package.1.0.0.nupkg", cancellationToken);

        var strict = CreateClient(new Dictionary<string, string> { { "Validation:RequireSignedPackages", "true" } });
        using (var package = new MemoryStream(signed))
        using (var request = TestPackages.PushRequest(package, apiKey: null))
        using (var response = await strict.SendAsync(request, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        Assert.Equal("Available", await WaitForResultAsync(strict, "Resigned.Package", "1.0.0"));
    }

    [Fact]
    public async Task SigningWithAnInsecureSourceUrlQuarantinesWithAReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (path, _) = CreateSigningCertificate();
        var client = CreateClient(new Dictionary<string, string>
        {
            { "Validation:RepositorySigningCertificatePath", path },
            { "Validation:RepositorySigningCertificatePassword", "test" },
            { "Validation:RepositorySigningServiceIndexUrl", "http://localhost/v3/index.json" },
        });

        await PushAsync(client, "Insecure.Package", "1.0.0");

        Assert.Equal("Quarantined", await WaitForResultAsync(client, "Insecure.Package", "1.0.0"));
        using var status = JsonDocument.Parse(await client.GetStringAsync("v3/package/Insecure.Package/1.0.0/validation", cancellationToken));
        var index = status.RootElement.GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("type").GetString() == "Index");
        Assert.Contains("https", index.GetProperty("message").GetString());
    }

    [Fact]
    public async Task WithoutASigningCertificateTheResourceIsAbsent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = CreateClient();

        Assert.DoesNotContain("RepositorySignatures", await client.GetStringAsync("v3/index.json", cancellationToken));
        using var response = await client.GetAsync("v3/repository-signatures/index.json", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateClient(
        Dictionary<string, string>? config = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var settings = new Dictionary<string, string> { { "Validation:Enabled", "true" } };
        foreach (var pair in config ?? new Dictionary<string, string>())
        {
            settings[pair.Key] = pair.Value;
        }

        var app = new BaGetApplication(_output, configOverrides: settings, configureServices: configureServices);
        var client = app.CreateClient();
        _disposables.Add(client);
        _disposables.Add(app);

        return client;
    }

    private static async Task<HttpStatusCode> PushAsync(HttpClient client, string id, string version)
    {
        using var package = TestPackages.Build(id, version);
        using var request = TestPackages.PushRequest(package, apiKey: null);
        using var response = await client.SendAsync(request);

        return response.StatusCode;
    }

    private static async Task<string?> StatusAsync(HttpClient client, string id, string version)
    {
        using var status = JsonDocument.Parse(await client.GetStringAsync($"v3/package/{id}/{version}/validation"));

        return status.RootElement.GetProperty("status").GetString();
    }

    private static async Task<string?> WaitForResultAsync(HttpClient client, string id, string version)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var status = await StatusAsync(client, id, version);
            if (status != "Validating" || DateTime.UtcNow > deadline) return status;

            await Task.Delay(50);
        }
    }

    private static async Task AssertNotServedAsync(HttpClient client, string id, string version)
    {
        var lower = id.ToLowerInvariant();

        using var download = await client.GetAsync($"v3/package/{lower}/{version}/{lower}.{version}.nupkg");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);

        using var registration = await client.GetAsync($"v3/registration/{lower}/index.json");
        Assert.Equal(HttpStatusCode.NotFound, registration.StatusCode);
    }

    /// <summary>
    /// A self-signed code signing certificate, as a PFX file, and its SHA-256 fingerprint.
    /// </summary>
    private (string Path, string Fingerprint) CreateSigningCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=BaGet Test Repository", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, // Code signing.
            false));

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        var path = Path.Combine(Path.GetTempPath(), "BaGetTests", Guid.NewGuid().ToString("N") + ".pfx");
        #pragma warning disable CS8604
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        #pragma warning restore CS8604
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, "test"));

        return (path, RepositorySignaturesController.Fingerprint(certificate.RawData));
    }

    public void Dispose()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
        {
            disposable.Dispose();
        }
    }

    private class GateValidator : IPackageValidator
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "Gate";

        public async Task<PackageValidatorResult> ValidateAsync(PackageValidationContext context, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

            return PackageValidatorResult.Valid;
        }
    }

    private class RejectingValidator : IPackageValidator
    {
        public string Name => "Rejecting";

        public Task<PackageValidatorResult> ValidateAsync(PackageValidationContext context, CancellationToken cancellationToken)
            => Task.FromResult(PackageValidatorResult.Invalid("Rejected by the test."));
    }
}