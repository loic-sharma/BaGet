using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Tests;

public class RawPackageContentTests : IClassFixture<ProtocolFixture>
{
    private readonly RawPackageContentClient _target;

    public RawPackageContentTests(ProtocolFixture fixture)
    {
        _target = fixture.ContentClient;
    }

    [Fact]
    public async Task GetsPackageVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetPackageVersionsOrNullAsync("Test.Package", cancellationToken);

        Assert.NotNull(result);
        #pragma warning disable CS8602
        Assert.Equal(2, result.Versions.Count);
        #pragma warning restore CS8602
        Assert.Equal("1.0.0", result.Versions[0]);
        Assert.Equal("2.0.0", result.Versions[1]);
    }

    [Fact]
    public async Task ReturnsNullIfPackageDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetPackageVersionsOrNullAsync(Guid.NewGuid().ToString(), cancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadsPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using (var stream = await _target.DownloadPackageOrNullAsync(
                   "Test.Package",
                   NuGetVersion.Parse("1.0.0"), cancellationToken))
        {
            Assert.NotNull(stream);

            using (var reader = new StreamReader(stream))
            {
                Assert.Equal(TestData.PackageContentNupkg, await reader.ReadToEndAsync(cancellationToken));
            }
        }
    }

    [Fact]
    public async Task DownloadsPackageUsingNormalizedLowercasedUrl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler();
        var target = new RawPackageContentClient(new HttpClient(handler), TestData.PackageContentUrl);

        using (await target.DownloadPackageOrNullAsync(
                   "Test.Package",
                   NuGetVersion.Parse("1.0.0.0+Build"), cancellationToken))
        {
        }

        var url = Assert.Single(handler.Requests);

        Assert.Equal(
            "/v3/content/test.package/1.0.0/test.package.1.0.0.nupkg",
            url.AbsolutePath);
    }

    [Fact]
    public async Task ReturnsNullIfPackageToDownloadDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stream = await _target.DownloadPackageOrNullAsync(
            Guid.NewGuid().ToString(),
            NuGetVersion.Parse("1.0.0"), cancellationToken);

        Assert.Null(stream);
    }

    [Fact]
    public async Task DownloadsPackageManifest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using (var stream = await _target.DownloadPackageManifestOrNullAsync(
                   "Test.Package",
                   NuGetVersion.Parse("1.0.0"), cancellationToken))
        {
            Assert.NotNull(stream);

            using (var reader = new StreamReader(stream))
            {
                Assert.Equal(TestData.PackageContentNuspec, await reader.ReadToEndAsync(cancellationToken));
            }
        }
    }

    [Fact]
    public async Task DownloadsPackageManifestUsingNormalizedLowercasedUrl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler();
        var target = new RawPackageContentClient(new HttpClient(handler), TestData.PackageContentUrl);

        using (await target.DownloadPackageManifestOrNullAsync(
                   "Test.Package",
                   NuGetVersion.Parse("1.0.0.0+Build"), cancellationToken))
        {
        }

        var url = Assert.Single(handler.Requests);

        Assert.Equal(
            "/v3/content/test.package/1.0.0/test.package.nuspec",
            url.AbsolutePath);
    }

    [Fact]
    public async Task ReturnsNullIfPackageManifestDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stream = await _target.DownloadPackageManifestOrNullAsync(
            Guid.NewGuid().ToString(),
            NuGetVersion.Parse("1.0.0"), cancellationToken);

        Assert.Null(stream);
    }
}