using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Search;

public class SearchResponseBuilderTests
{
    private readonly Mock<IUrlGenerator> _url;
    private readonly SearchResponseBuilder _target;

    public SearchResponseBuilderTests()
    {
        _url = new Mock<IUrlGenerator>();
        _url.Setup(u => u.GetRegistrationIndexUrl(It.IsAny<string>()))
            .Returns<string>(id => $"https://test/v3/registration/{id}/index.json");
        _url.Setup(u => u.GetRegistrationLeafUrl(It.IsAny<string>(), It.IsAny<NuGetVersion>()))
            .Returns<string, NuGetVersion>((id, v) => $"https://test/v3/registration/{id}/{v}.json");
        _url.Setup(u => u.GetPackageIconDownloadUrl(It.IsAny<string>(), It.IsAny<NuGetVersion>()))
            .Returns<string, NuGetVersion>((id, v) => $"https://test/v3/package/{id}/{v}/icon");
        _url.Setup(u => u.GetPackageMetadataResourceUrl())
            .Returns("https://test/v3/registration");

        _target = new SearchResponseBuilder(_url.Object);
    }

    private static Package Package(string id, string version, long downloads = 0, bool embeddedIcon = false)
        => new()
        {
            Id = id,
            Version = NuGetVersion.Parse(version),
            Downloads = downloads,
            HasEmbeddedIcon = embeddedIcon,
            Listed = true,
            Authors = new[] { "author" },
            Tags = new[] { "tag" },
            Description = "description",
            Summary = "summary",
            Title = "title",
            Dependencies = new List<PackageDependency>(),
            PackageTypes = new List<PackageType>(),
            TargetFrameworks = new List<TargetFramework>(),
        };

    private static PackageRegistration Registration(string? id, params Package[] packages) => new(id, packages);

    [Fact]
    public void ReportsTheLatestVersionAsTheResultVersion()
    {
        var response = _target.BuildSearch(new[]
        {
            Registration("Test",
                Package("Test", "1.0.0"),
                Package("Test", "3.1.0"),
                Package("Test", "2.0.0")),
        }, totalHits: 1);

        #pragma warning disable CS8604
        Assert.Equal("3.1.0", Assert.Single(response.Data).Version);
        #pragma warning restore CS8604
    }

    [Fact]
    public void APrereleaseVersionSortsBelowItsRelease()
    {
        var response = _target.BuildSearch(new[]
        {
            Registration("Test",
                Package("Test", "2.0.0-beta"),
                Package("Test", "2.0.0")),
        }, totalHits: 1);

        #pragma warning disable CS8604
        Assert.Equal("2.0.0", Assert.Single(response.Data).Version);
        #pragma warning restore CS8604
    }

    [Fact]
    public void ComparesVersionsNumericallyNotAsText()
    {
        var response = _target.BuildSearch(new[]
        {
            Registration("Test",
                Package("Test", "9.0.0"),
                Package("Test", "10.0.0")),
        }, totalHits: 1);

        #pragma warning disable CS8604
        Assert.Equal("10.0.0", Assert.Single(response.Data).Version);
        #pragma warning restore CS8604
    }

    [Fact]
    public void ListsVersionsNewestFirst()
    {
        var response = _target.BuildSearch(new[]
        {
            Registration("Test",
                Package("Test", "1.0.0"),
                Package("Test", "3.0.0"),
                Package("Test", "2.0.0")),
        }, totalHits: 1);

        #pragma warning disable CS8604
        Assert.Equal(
            new[] { "3.0.0", "2.0.0", "1.0.0" },
            Assert.Single(response.Data).Versions?.Select(v => v.Version));
        #pragma warning restore CS8604
    }

    [Fact]
    public void TotalDownloadsSumsEveryVersion()
    {
        var response = _target.BuildSearch(new[]
        {
            Registration("Test",
                Package("Test", "1.0.0", downloads: 10),
                Package("Test", "2.0.0", downloads: 32)),
        }, totalHits: 1);

        #pragma warning disable CS8604
        Assert.Equal(42, Assert.Single(response.Data).TotalDownloads);
        #pragma warning restore CS8604
    }

    [Fact]
    public void EachVersionKeepsItsOwnDownloadCount()
    {
        var response = _target.BuildSearch(new[]
        {
            Registration("Test",
                Package("Test", "1.0.0", downloads: 10),
                Package("Test", "2.0.0", downloads: 32)),
        }, totalHits: 1);

        #pragma warning disable CS8604
        var versions = Assert.Single(response.Data).Versions;
        #pragma warning restore CS8604

        Assert.Equal(32, versions?.First(v => v.Version == "2.0.0").Downloads);
        Assert.Equal(10, versions?.First(v => v.Version == "1.0.0").Downloads);
    }

    [Fact]
    public void UsesTheGeneratedIconUrlForAnEmbeddedIcon()
    {
        var package = Package("Test", "1.0.0", embeddedIcon: true);

        var response = _target.BuildSearch(new[] { Registration("Test", package) }, totalHits: 1);

        #pragma warning disable CS8604
        Assert.Equal("https://test/v3/package/Test/1.0.0/icon", Assert.Single(response.Data).IconUrl);
        #pragma warning restore CS8604
    }

    [Fact]
    public void UsesThePackagesOwnIconUrlWhenTheIconIsNotEmbedded()
    {
        var package = Package("Test", "1.0.0");
        package.IconUrl = new Uri("https://elsewhere.test/icon.png");

        var response = _target.BuildSearch(new[] { Registration("Test", package) }, totalHits: 1);

        #pragma warning disable CS8604
        Assert.Equal("https://elsewhere.test/icon.png", Assert.Single(response.Data).IconUrl);
        #pragma warning restore CS8604
    }

    [Fact]
    public void ReturnsOneResultPerRegistration()
    {
        var response = _target.BuildSearch(new[]
        {
            Registration("A", Package("A", "1.0.0")),
            Registration("B", Package("B", "1.0.0")),
        }, totalHits: 2);

        Assert.Equal(new[] { "A", "B" }, response.Data?.Select(d => d.PackageId));
    }

    [Fact]
    public void ReturnsAnEmptyResponseForNoRegistrations()
    {
        var response = _target.BuildSearch(new PackageRegistration[0], totalHits: 0);

        #pragma warning disable CS8604
        Assert.Empty(response.Data);
        #pragma warning restore CS8604
        Assert.Equal(0, response.TotalHits);
    }

    [Fact]
    public void BuildAutocompleteReturnsTheIdsItWasGiven()
    {
        var response = _target.BuildAutocomplete(new[] { "First", "Second" }, totalHits: 2);

        Assert.Equal(new[] { "First", "Second" }, response.Data);
    }

    [Fact]
    public void BuildAutocompleteHandlesAnEmptyList()
    {
        var response = _target.BuildAutocomplete(new string[0], totalHits: 0);

        #pragma warning disable CS8604
        Assert.Empty(response.Data);
        #pragma warning restore CS8604
        Assert.Equal(0, response.TotalHits);
    }

    [Fact]
    public void TotalHitsIsTheTotalGivenRatherThanThePageSize()
    {
        // The protocol documents TotalHits as the number of matches disregarding skip
        // and take, so a page of one out of fifty reports fifty.
        var response = _target.BuildSearch(
            new[] { Registration("Test", Package("Test", "1.0.0")) },
            totalHits: 50);

        #pragma warning disable CS8604
        Assert.Single(response.Data);
        #pragma warning restore CS8604
        Assert.Equal(50, response.TotalHits);
    }

    [Fact]
    public void AutocompleteTotalHitsIsTheTotalGivenRatherThanThePageSize()
    {
        var response = _target.BuildAutocomplete(new[] { "Only.One" }, totalHits: 25);

        #pragma warning disable CS8604
        Assert.Single(response.Data);
        #pragma warning restore CS8604
        Assert.Equal(25, response.TotalHits);
    }

    [Fact]
    public void BuildDependentsReturnsTheDependentsItWasGiven()
    {
        var dependents = new[]
        {
            new PackageDependent { Id = "Dependent", Description = "d", TotalDownloads = 5 },
        };

        var response = _target.BuildDependents(dependents);

        #pragma warning disable CS8604
        Assert.Equal("Dependent", Assert.Single(response.Data).Id);
        #pragma warning restore CS8604
    }
}