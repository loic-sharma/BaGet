using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Tests;

public class NuGetClientTests : IClassFixture<ProtocolFixture>
{
    private readonly NuGetClient _target;

    public NuGetClientTests(ProtocolFixture fixture)
    {
        _target = fixture.NuGetClient;
    }

    [Fact]
    public async Task DownloadsPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using (var stream = await _target.DownloadPackageAsync("Test.Package", NuGetVersion.Parse("1.0.0"), cancellationToken))
        using (var reader = new StreamReader(stream))
        {
            Assert.Equal(TestData.PackageContentNupkg, await reader.ReadToEndAsync(cancellationToken));
        }
    }

    [Fact]
    public async Task ThrowsIfPackageToDownloadDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var exception = await Assert.ThrowsAsync<PackageNotFoundException>(
            () => _target.DownloadPackageAsync("Test.Package", NuGetVersion.Parse("9.9.9"), cancellationToken));

        Assert.Equal("Test.Package", exception.PackageId);
        Assert.Equal(NuGetVersion.Parse("9.9.9"), exception.PackageVersion);
    }

    [Fact]
    public async Task DownloadsPackageManifest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using (var stream = await _target.DownloadPackageManifestAsync("Test.Package", NuGetVersion.Parse("1.0.0"), cancellationToken))
        using (var reader = new StreamReader(stream))
        {
            Assert.Equal(TestData.PackageContentNuspec, await reader.ReadToEndAsync(cancellationToken));
        }
    }

    [Fact]
    public async Task ThrowsIfPackageManifestToDownloadDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var exception = await Assert.ThrowsAsync<PackageNotFoundException>(
            () => _target.DownloadPackageManifestAsync("Test.Package", NuGetVersion.Parse("9.9.9"), cancellationToken));

        Assert.Equal("Test.Package", exception.PackageId);
        Assert.Equal(NuGetVersion.Parse("9.9.9"), exception.PackageVersion);
    }

    [Fact]
    public async Task ListsPackageVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The listed versions come from the package's registration index, which,
        // for this package, has its items inlined.
        var versions = await _target.ListPackageVersionsAsync("Test.Package", cancellationToken);

        Assert.Equal(
            new[] { "1.0.0", "2.0.0+build", "3.0.0" },
            versions.Select(v => v.ToFullString()).ToArray());
    }

    [Fact]
    public async Task ListsPackageVersionsSkippingUnlistedVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // "Paged.Package" has three versions, 2.0.0+build is unlisted.
        var versions = await _target.ListPackageVersionsAsync("Paged.Package", cancellationToken);

        Assert.Equal(
            new[] { "1.0.0", "3.0.0" },
            versions.Select(v => v.ToFullString()).ToArray());
    }

    [Fact]
    public async Task ListsPackageVersionsOfPackageThatDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var versions = await _target.ListPackageVersionsAsync(Guid.NewGuid().ToString(), cancellationToken);

        Assert.Empty(versions);
    }

    [Fact]
    public async Task ListsAllPackageVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Unlisted versions are only served by the package content resource,
        // which lists 2.0.0 even though the registration marks it unlisted.
        var versions = await _target.ListPackageVersionsAsync("Paged.Package", includeUnlisted: true, cancellationToken);

        Assert.Equal(
            new[] { "1.0.0", "2.0.0", "3.0.0" },
            versions.Select(v => v.ToFullString()).ToArray());
    }

    [Fact]
    public async Task ListsAllPackageVersionsOfPackageThatDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var versions = await _target.ListPackageVersionsAsync(
            Guid.NewGuid().ToString(),
            includeUnlisted: true, cancellationToken);

        Assert.Empty(versions);
    }

    [Fact]
    public async Task GetsPackageMetadataInlined()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var metadata = await _target.GetPackageMetadataAsync("Test.Package", cancellationToken);

        Assert.Equal(3, metadata.Count);
        Assert.All(metadata, m => Assert.Equal("Test.Package", m.PackageId));
        Assert.Equal(
            new[] { "1.0.0", "2.0.0+build", "3.0.0" },
            metadata.Select(m => m.Version).ToArray());
    }

    [Fact]
    public async Task GetsPackageMetadataPaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // "Paged.Package"'s registration index does not inline its items, so
        // each registration page has to be fetched separately.
        var metadata = await _target.GetPackageMetadataAsync("Paged.Package", cancellationToken);

        Assert.Equal(3, metadata.Count);
        Assert.All(metadata, m => Assert.Equal("Paged.Package", m.PackageId));
        Assert.Equal(
            new[] { "1.0.0", "2.0.0+build", "3.0.0" },
            metadata.Select(m => m.Version).ToArray());
        Assert.Equal(
            new bool?[] { true, false, true },
            metadata.Select(m => m.Listed).ToArray());
    }

    [Fact]
    public async Task GetsPackageMetadataOfPackageThatDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var metadata = await _target.GetPackageMetadataAsync(Guid.NewGuid().ToString(), cancellationToken);

        Assert.Empty(metadata);
    }

    [Fact]
    public async Task GetsSinglePackageMetadataInlined()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var metadata = await _target.GetPackageMetadataAsync("Test.Package", NuGetVersion.Parse("2.0.0"), cancellationToken);

        #pragma warning disable CS8602
        Assert.Equal("Test.Package", metadata.PackageId);
        #pragma warning restore CS8602
        Assert.Equal("2.0.0+build", metadata.Version);
        Assert.Equal("Test Author", metadata.Authors);
    }

    [Fact]
    public async Task GetsSinglePackageMetadataPaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var metadata = await _target.GetPackageMetadataAsync("Paged.Package", NuGetVersion.Parse("3.0.0"), cancellationToken);

        #pragma warning disable CS8602
        Assert.Equal("Paged.Package", metadata.PackageId);
        #pragma warning restore CS8602
        Assert.Equal("3.0.0", metadata.Version);
        Assert.True(metadata.Listed);
    }

    [Fact]
    public async Task ThrowsIfSinglePackageMetadataVersionDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<PackageNotFoundException>(
            () => _target.GetPackageMetadataAsync("Test.Package", NuGetVersion.Parse("9.9.9"), cancellationToken));
    }

    [Fact]
    public async Task ThrowsIfSinglePackageMetadataPackageDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<PackageNotFoundException>(
            () => _target.GetPackageMetadataAsync(Guid.NewGuid().ToString(), NuGetVersion.Parse("1.0.0"), cancellationToken));
    }

    [Fact]
    public async Task Searches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var results = await _target.SearchAsync(cancellationToken: cancellationToken);

        #pragma warning disable CS8604
        var result = Assert.Single(results);
        #pragma warning restore CS8604

        Assert.Equal("Test.Package", result.PackageId);
        Assert.Equal("3.0.0", result.Version);
        #pragma warning disable CS8604
        Assert.Equal("Package Authors", Assert.Single(result.Authors));
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task Autocompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        #pragma warning disable CS8602
        var results = await _target.AutocompleteAsync(cancellationToken: cancellationToken);
        #pragma warning restore CS8602

        #pragma warning disable CS8604
        Assert.Equal("Test.Package", Assert.Single(results));
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task PackageExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.True(await _target.ExistsAsync("Test.Package", cancellationToken));
        Assert.True(await _target.ExistsAsync("Test.Package", NuGetVersion.Parse("1.0.0"), cancellationToken));
    }

    [Fact]
    public async Task PackageDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.False(await _target.ExistsAsync(Guid.NewGuid().ToString(), cancellationToken));
        Assert.False(await _target.ExistsAsync("Test.Package", NuGetVersion.Parse("9.9.9"), cancellationToken));
    }
}