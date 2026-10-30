using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests;

public class RawServiceIndexClientTests : IClassFixture<ProtocolFixture>
{
    private readonly RawServiceIndexClient _target;

    public RawServiceIndexClientTests(ProtocolFixture fixture)
    {
        _target = fixture.ServiceIndexClient;
    }

    [Fact]
    public async Task GetsServiceIndex()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetAsync(cancellationToken);

        #pragma warning disable CS8602
        Assert.Equal("3.0.0", result.Version);
        Assert.Equal(5, result.Resources.Count);
        #pragma warning restore CS8602

        Assert.Equal(TestData.CatalogIndexUrl, result.GetCatalogResourceUrl());
        Assert.Equal(TestData.PackageMetadataUrl, result.GetPackageMetadataResourceUrl());
        Assert.Equal(TestData.PackageContentUrl, result.GetPackageContentResourceUrl());
        Assert.Equal(TestData.SearchUrl, result.GetSearchQueryResourceUrl());
        Assert.Equal(TestData.AutocompleteUrl, result.GetSearchAutocompleteResourceUrl());
    }
}