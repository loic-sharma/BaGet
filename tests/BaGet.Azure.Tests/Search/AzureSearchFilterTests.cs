using System.Collections.Generic;
using System.Linq;
using BaGet.Core;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Azure.Tests.Search;

/// <summary>
/// Covers the OData filter that <see cref="AzureSearchService"/> uses to pick the document for
/// a set of search filters. <see cref="IndexActionBuilder"/> writes one document per
/// <see cref="SearchFilters"/> value, so the filter's value has to be exactly what the indexer
/// writes, or a query silently matches nothing - or every copy of the same package.
/// </summary>
public class AzureSearchFilterTests
{
    [Theory]
    [InlineData(false, false, "searchFilters eq 'Default'")]
    [InlineData(true, false, "searchFilters eq 'IncludePrerelease'")]
    [InlineData(false, true, "searchFilters eq 'IncludeSemVer2'")]
    [InlineData(true, true, "searchFilters eq 'IncludePrereleaseAndSemVer2'")]
    public void BuildsTheFilterForEachCombination(bool prerelease, bool semVer2, string expected)
    {
        Assert.Equal(expected, AzureSearchService.BuildSearchFilter(prerelease, semVer2));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MatchesExactlyOneIndexedDocument(bool prerelease, bool semVer2)
    {
        var documents = IndexedDocuments();
        var filter = AzureSearchService.BuildSearchFilter(prerelease, semVer2);

        // Four documents are indexed for this registration, one per search filter.
        Assert.Equal(4, documents.Count);
        Assert.Single(documents, d => $"searchFilters eq '{d.SearchFilters}'" == filter);
    }

    private static List<PackageDocument> IndexedDocuments()
    {
        // A stable, a prerelease and a SemVer 2.0.0 version, so that none of the four search
        // filter combinations is empty.
        var registration = new PackageRegistration(
            "Test",
            new[]
            {
                Package("1.0.0", prerelease: false, semVerLevel: SemVerLevel.Unknown),
                Package("2.0.0-beta", prerelease: true, semVerLevel: SemVerLevel.Unknown),
                Package("3.0.0+build", prerelease: false, semVerLevel: SemVerLevel.SemVer2),
            });

        return new IndexActionBuilder()
            .AddPackage(registration)
            .Select(a => a.Document)
            .OfType<PackageDocument>()
            .ToList();
    }

    private static Package Package(string version, bool prerelease, SemVerLevel semVerLevel)
        => new()
        {
            Id = "Test",
            Version = NuGetVersion.Parse(version),
            Listed = true,
            IsPrerelease = prerelease,
            SemVerLevel = semVerLevel,
            Authors = new[] { "author" },
            Tags = new[] { "tag" },
            Dependencies = new List<PackageDependency>(),
            PackageTypes = new List<PackageType>(),
            TargetFrameworks = new List<TargetFramework>(),
        };
}