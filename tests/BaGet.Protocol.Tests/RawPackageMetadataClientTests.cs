using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests;

public class RawPackageMetadataClientTests : IClassFixture<ProtocolFixture>
{
    private readonly RawPackageMetadataClient _target;

    public RawPackageMetadataClientTests(ProtocolFixture fixture)
    {
        _target = fixture.MetadataClient;
    }

    [Fact]
    public async Task GetRegistrationIndexInlinedItems()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetRegistrationIndexOrNullAsync("Test.Package", cancellationToken);

        Assert.NotNull(result);
        #pragma warning disable CS8602
        Assert.Equal(2, result.Pages.Count);
        #pragma warning restore CS8602

        Assert.True(result.Pages[0].Count == 1);
        #pragma warning disable CS8602
        Assert.True(result.Pages[0].ItemsOrNull.Count == 1);
        #pragma warning restore CS8602
        Assert.Equal("1.0.0", result.Pages[0].Lower);
        Assert.Equal("1.0.0", result.Pages[0].Upper);
        Assert.StartsWith(TestData.RegistrationIndexInlinedItemsUrl, result.Pages[0].RegistrationPageUrl);

        Assert.True(result.Pages[1].Count == 2);
        #pragma warning disable CS8602
        Assert.True(result.Pages[1].ItemsOrNull.Count == 2);
        #pragma warning restore CS8602
        Assert.Equal("2.0.0", result.Pages[1].Lower);
        Assert.Equal("3.0.0", result.Pages[1].Upper);
        Assert.StartsWith(TestData.RegistrationIndexInlinedItemsUrl, result.Pages[1].RegistrationPageUrl);
    }

    [Fact]
    public async Task GetRegistrationIndexPagedItems()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetRegistrationIndexOrNullAsync("Paged.Package", cancellationToken);

        Assert.NotNull(result);
        #pragma warning disable CS8602
        Assert.Equal(2, result.Pages.Count);
        #pragma warning restore CS8602

        Assert.True(result.Pages[0].Count == 1);
        Assert.Null(result.Pages[0].ItemsOrNull);
        Assert.Equal("1.0.0", result.Pages[0].Lower);
        Assert.Equal("1.0.0", result.Pages[0].Upper);

        Assert.True(result.Pages[1].Count == 2);
        Assert.Null(result.Pages[1].ItemsOrNull);
        Assert.Equal("2.0.0", result.Pages[1].Lower);
        Assert.Equal("3.0.0", result.Pages[1].Upper);
    }

    [Fact]
    public async Task GetRegistrationPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetRegistrationPageAsync(TestData.RegistrationPageUrl, cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.NotNull(result.ItemsOrNull);
        Assert.Equal(2, result.ItemsOrNull.Count);
        Assert.Equal("2.0.0", result.Lower);
        Assert.Equal("3.0.0", result.Upper);

        var firstMetadata = result.ItemsOrNull[0].PackageMetadata;

        #pragma warning disable CS8602
        Assert.Equal("Paged.Package", firstMetadata.PackageId);
        #pragma warning restore CS8602
        Assert.Equal("2.0.0+build", firstMetadata.Version);
    }

    [Fact]
    public async Task GetRegistrationPageDeprecated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetRegistrationPageAsync(TestData.RegistrationPageDeprecatedUrl, cancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.ItemsOrNull);

        var metadata = Assert.Single(result.ItemsOrNull).PackageMetadata;

        #pragma warning disable CS8602
        Assert.Equal("Deprecated.Package", metadata.PackageId);
        #pragma warning restore CS8602
        Assert.Equal("1.0.0", metadata.Version);

        var deprecation = metadata.Deprecation;

        Assert.NotNull(deprecation);
        #pragma warning disable CS8604
        Assert.Equal(new[] { "Legacy", "CriticalBugs" }, deprecation.Reasons.ToArray());
        #pragma warning restore CS8604
        Assert.Equal(
            "This package is deprecated, use the alternate package instead.",
            deprecation.Message);

        Assert.NotNull(deprecation.AlternatePackage);
        Assert.Equal("Test.Package", deprecation.AlternatePackage.Id);
        Assert.Equal("[2.0.0, )", deprecation.AlternatePackage.Range);
    }

    [Fact]
    public async Task GetRegistrationPageWithoutDeprecation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetRegistrationPageAsync(TestData.RegistrationPageUrl, cancellationToken);

        Assert.NotNull(result);
        #pragma warning disable CS8602, CS8604
        Assert.All(result.ItemsOrNull, item => Assert.Null(item.PackageMetadata.Deprecation));
        #pragma warning restore CS8602, CS8604
    }

    [Fact]
    public async Task ReturnsNullIfRegistrationIndexDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetRegistrationIndexOrNullAsync(Guid.NewGuid().ToString(), cancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task ThrowsIfRegistrationPageDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _target.GetRegistrationPageAsync(
                $"https://test.example/v3/metadata/{Guid.NewGuid()}/page/1.0.0/1.0.0.json", cancellationToken));
    }

    [Fact]
    public async Task GetsRegistrationLeaf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetRegistrationLeafAsync(TestData.RegistrationLeafListedUrl, cancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Listed);
        Assert.Equal(2010, result.Published.Year);

        Assert.Equal(
            "https://test.example/v3/content/test.package/1.0.0/test.package.1.0.0.nupkg",
            result.PackageContentUrl);
        Assert.Equal(
            "https://test.example/v3/metadata/test.package/index.json",
            result.RegistrationIndexUrl);
        Assert.Equal(
            "https://test.example/v3/metadata/test.package/1.0.0.json",
            result.RegistrationLeafUrl);
    }

    [Fact]
    public async Task GetsRegistrationLeafUnlisted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await _target.GetRegistrationLeafAsync(TestData.RegistrationLeafUnlistedUrl, cancellationToken);

        Assert.NotNull(result);
        Assert.False(result.Listed);
        Assert.Equal(2010, result.Published.Year);

        Assert.Equal(
            "https://test.example/v3/content/paged.package/2.0.0/paged.package.2.0.0.nupkg",
            result.PackageContentUrl);
        Assert.Equal(
            "https://test.example/v3/metadata/paged.package/index.json",
            result.RegistrationIndexUrl);
        Assert.Equal(
            "https://test.example/v3/metadata/paged.package/2.0.0.json",
            result.RegistrationLeafUrl);
    }
}