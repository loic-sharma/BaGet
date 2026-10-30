using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests;

public class RawCatalogClientTests : IClassFixture<ProtocolFixture>
{
    private readonly RawCatalogClient _target;

    public RawCatalogClientTests(ProtocolFixture fixture)
    {
        _target = fixture.CatalogClient;
    }

    [Fact]
    public async Task GetCatalogIndex()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetIndexAsync(cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        #pragma warning disable CS8602
        Assert.Equal(2, result.Items.Count);
        #pragma warning restore CS8602
        Assert.Equal(TestData.CatalogPageUrl, result.Items.Select(i => i.CatalogPageUrl).First());
    }

    [Fact]
    public async Task GetCatalogPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var page = await _target.GetPageAsync(TestData.CatalogPageUrl, cancellationToken);

        Assert.Equal(2, page.Count);
        #pragma warning disable CS8602
        Assert.Equal(2, page.Items.Count);
        #pragma warning restore CS8602
        Assert.Equal(TestData.CatalogIndexUrl, page.CatalogIndexUrl);
        Assert.Equal(TestData.PackageDetailsCatalogLeafUrl, page.Items[0].CatalogLeafUrl);
        Assert.Equal(TestData.PackageDeleteCatalogLeafUrl, page.Items[1].CatalogLeafUrl);
    }

    [Fact]
    public async Task GetPackageDetailsLeaf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var leaf = await _target.GetPackageDetailsLeafAsync(TestData.PackageDetailsCatalogLeafUrl, cancellationToken);

        Assert.Equal(TestData.PackageDetailsCatalogLeafUrl, leaf.CatalogLeafUrl);
        #pragma warning disable CS8602
        Assert.Equal("PackageDetails", leaf.Type[0]);
        #pragma warning restore CS8602
        Assert.Equal("catalog:Permalink", leaf.Type[1]);

        Assert.Equal("Test.Package", leaf.PackageId);
        Assert.Equal("1.0.0", leaf.PackageVersion);
    }

    [Fact]
    public async Task GetPackageDeleteLeaf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var leaf = await _target.GetPackageDeleteLeafAsync(TestData.PackageDeleteCatalogLeafUrl, cancellationToken);

        Assert.Equal(TestData.PackageDeleteCatalogLeafUrl, leaf.CatalogLeafUrl);
        #pragma warning disable CS8602
        Assert.Equal("PackageDelete", leaf.Type[0]);
        #pragma warning restore CS8602
        Assert.Equal("catalog:Permalink", leaf.Type[1]);

        Assert.Equal("Deleted.Package", leaf.PackageId);
        Assert.Equal("1.0.0", leaf.PackageVersion);
    }

    [Fact]
    public async Task ThrowsOnTypeMismatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentException>(() => _target.GetPackageDetailsLeafAsync(TestData.PackageDeleteCatalogLeafUrl, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => _target.GetPackageDeleteLeafAsync(TestData.PackageDetailsCatalogLeafUrl, cancellationToken));
    }
}