using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Telemetry;

[Collection(DiagnosticsCapture.Name)]
public class BaGetDiagnosticsTests : IDisposable
{
    private readonly DiagnosticsCapture _capture = new();

    public void Dispose() => _capture.Dispose();

    private static PackageIndexingService Indexer(Mock<IPackageStorageService>? storage = null)
    {
        var packages = new Mock<IPackageDatabase>();
        packages.Setup(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>())).ReturnsAsync(PackageAddResult.Success);

        storage ??= new Mock<IPackageStorageService>();
        storage
            .Setup(s => s.SavePackageContentAsync(
                It.IsAny<Package>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackageSaveResult.Success);

        var options = new Mock<IOptionsSnapshot<BaGetOptions>>();
        options.Setup(o => o.Value).Returns(new BaGetOptions());

        return new PackageIndexingService(
            packages.Object, storage.Object, Mock.Of<ISearchIndexer>(), new SystemTime(), options.Object,
            Mock.Of<ILogger<PackageIndexingService>>());
    }

    [Fact]
    public void WithoutAListenerNoActivityIsStarted()
    {
        _capture.Dispose();

        // Another test class may hold a listener only if it is not in this collection; none is.
        Assert.False(BaGetDiagnostics.Source.HasListeners());
        Assert.Null(BaGetDiagnostics.Source.StartActivity(BaGetDiagnostics.Spans.PackageIndex));
    }

    [Fact]
    public async Task IndexingSpanCarriesTheIdVersionAndResultAndCountsAPush()
    {
        using var stream = TestPackage.CreateMinimal(id: "Diag.Index", version: "1.2.3");

        var result = await Indexer().IndexAsync(stream, CancellationToken.None);

        Assert.Equal(PackageIndexingResult.Success, result);
        var span = Assert.Single(_capture.Spans("package.index"), s => (string?)DiagnosticsCapture.Tag(s, BaGetDiagnostics.Tags.PackageId) == "Diag.Index");
        Assert.Equal("1.2.3", DiagnosticsCapture.Tag(span, BaGetDiagnostics.Tags.PackageVersion));
        Assert.Equal("Success", DiagnosticsCapture.Tag(span, BaGetDiagnostics.Tags.Result));
        Assert.NotEqual(ActivityStatusCode.Error, span.Status);

        var pushed = Assert.Single(_capture.Counts("baget.packages.pushed"));
        Assert.Equal(1, pushed.Value);
        Assert.Equal("Success", pushed.Tags[BaGetDiagnostics.Tags.Result]);
    }

    [Fact]
    public async Task AnInvalidPackageIsRecordedWithItsResult()
    {
        using var stream = TestPackage.NotAZipArchive();

        await Indexer().IndexAsync(stream, CancellationToken.None);

        var span = Assert.Single(_capture.Spans("package.index"));
        Assert.Equal("InvalidPackage", DiagnosticsCapture.Tag(span, BaGetDiagnostics.Tags.Result));
        Assert.Equal("InvalidPackage", Assert.Single(_capture.Counts("baget.packages.pushed")).Tags[BaGetDiagnostics.Tags.Result]);
    }

    [Fact]
    public async Task AFailureMarksTheSpanAsAnErrorWithoutTheExceptionMessage()
    {
        var storage = new Mock<IPackageStorageService>();
        var indexer = Indexer(storage);
        storage
            .Setup(s => s.SavePackageContentAsync(
                It.IsAny<Package>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Server=db;Password=hunter2"));
        using var stream = TestPackage.CreateMinimal(id: "Diag.Fail", version: "1.0.0");

        await Assert.ThrowsAsync<IOException>(() => indexer.IndexAsync(stream, CancellationToken.None));

        var span = Assert.Single(_capture.Spans("package.index"));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("error", DiagnosticsCapture.Tag(span, BaGetDiagnostics.Tags.Result));
        Assert.DoesNotContain("hunter2", span.StatusDescription ?? string.Empty);
        Assert.DoesNotContain(span.TagObjects, t => t.Value?.ToString()?.Contains("hunter2") == true);
        Assert.Equal("error", Assert.Single(_capture.Counts("baget.packages.pushed")).Tags[BaGetDiagnostics.Tags.Result]);
    }

    [Fact]
    public async Task AMirrorFetchSpanParentsThePackageIndexSpan()
    {
        var version = new NuGetVersion("1.0.0");
        var upstream = new Mock<IUpstreamClient>();
        upstream.Setup(u => u.DownloadPackageOrNullAsync("Diag.Mirror", version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => TestPackage.CreateMinimal(id: "Diag.Mirror", version: "1.0.0"));
        var service = new PackageService(Mock.Of<IPackageDatabase>(), upstream.Object, Indexer(), Mock.Of<ILogger<PackageService>>());

        Assert.True(await service.ExistsAsync("Diag.Mirror", version, CancellationToken.None));

        var fetch = Assert.Single(_capture.Spans("mirror.fetch"));
        var index = Assert.Single(_capture.Spans("package.index"));
        Assert.Equal("Diag.Mirror", DiagnosticsCapture.Tag(fetch, BaGetDiagnostics.Tags.PackageId));
        Assert.Equal("1.0.0", DiagnosticsCapture.Tag(fetch, BaGetDiagnostics.Tags.PackageVersion));
        Assert.Equal("Success", DiagnosticsCapture.Tag(fetch, BaGetDiagnostics.Tags.Result));
        Assert.Equal(fetch.Id, index.ParentId);
        Assert.Equal(fetch.TraceId, index.TraceId);
    }

    [Fact]
    public async Task AMirrorMissRecordsNotFound()
    {
        var service = new PackageService(
            Mock.Of<IPackageDatabase>(), Mock.Of<IUpstreamClient>(), Indexer(), Mock.Of<ILogger<PackageService>>());

        Assert.False(await service.ExistsAsync("Diag.Missing", new NuGetVersion("1.0.0"), CancellationToken.None));

        Assert.Equal("NotFound", DiagnosticsCapture.Tag(Assert.Single(_capture.Spans("mirror.fetch")), BaGetDiagnostics.Tags.Result));
    }

    [Fact]
    public async Task ADownloadIsCounted()
    {
        var service = new PackageService(
            Mock.Of<IPackageDatabase>(), Mock.Of<IUpstreamClient>(), Indexer(), Mock.Of<ILogger<PackageService>>());

        await service.AddDownloadAsync("Diag.Download", new NuGetVersion("1.0.0"), CancellationToken.None);

        var count = Assert.Single(_capture.Counts("baget.downloads"));
        Assert.Equal(1, count.Value);
        Assert.Empty(count.Tags);
    }
}
