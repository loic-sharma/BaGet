using System.Collections.Generic;
using BaGet.Core;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Azure.Tests.Search;

public class AzureAutocompleteQueryTests
{
    [Fact]
    public void PrefixMatchesTheWholeIdAndKeepsTheAnalyzedMatch()
    {
        // The analyzed match keeps documents indexed before "normalizedId" existed findable.
        Assert.Equal(
            @"+(normalizedId:newtonsoft.js* OR newtonsoft.js)",
            AzureSearchService.BuildAutocompleteQuery("Newtonsoft.Js", packageType: null));
    }

    [Fact]
    public void EscapesTheQuery()
    {
        Assert.Equal(
            @"+(normalizedId:a\:b\)* OR a\:b\))",
            AzureSearchService.BuildAutocompleteQuery("a:b)", packageType: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("*")]
    public void AnEmptyQueryMatchesEverything(string? query)
    {
        Assert.Equal("*", AzureSearchService.BuildAutocompleteQuery(query, packageType: null));
    }

    [Fact]
    public void AddsThePackageTypeAsARequiredClause()
    {
        Assert.Equal(
            @"+(normalizedId:foo* OR foo) +packageTypes:""DotnetTool""",
            AzureSearchService.BuildAutocompleteQuery("foo", "DotnetTool"));
    }

    [Fact]
    public void APackageTypeAloneIsAValidQuery()
    {
        Assert.Equal(@"+packageTypes:""DotnetTool""", AzureSearchService.BuildAutocompleteQuery(null, "DotnetTool"));
    }

    [Fact]
    public void TheVersionsFilterIsAnExactLowercaseMatch()
    {
        Assert.Equal(
            "normalizedId eq 'newtonsoft.json' and searchFilters eq 'Default'",
            AzureSearchService.BuildVersionsFilter("Newtonsoft.Json", "searchFilters eq 'Default'"));
    }

    [Fact]
    public void TheVersionsFilterEscapesAQuote()
    {
        Assert.Equal(
            "normalizedId eq 'a''b' and x",
            AzureSearchService.BuildVersionsFilter("a'b", "x"));
    }

    [Fact]
    public void IndexedDocumentsCarryTheNormalizedId()
    {
        var package = new Package
        {
            Id = "Newtonsoft.Json",
            Version = NuGetVersion.Parse("1.0.0"),
            Authors = new string[0],
            Tags = new string[0],
            Listed = true,
            Dependencies = new List<PackageDependency>(),
            PackageTypes = new List<PackageType>(),
            TargetFrameworks = new List<TargetFramework>(),
        };

        var actions = new IndexActionBuilder().AddPackage(new PackageRegistration("Newtonsoft.Json", new[] { package }));

        Assert.All(actions, a => Assert.Equal("newtonsoft.json", ((PackageDocument)a.Document).NormalizedId));
    }
}

public class TableSearchFilterTests
{
    [Fact]
    public void TheSearchPrefixIsLowercasedToMatchThePartitionKeys()
    {
        var filter = TableSearchService.GenerateSearchFilter("Newtonsoft", includePrerelease: true, includeSemVer2: true);

        Assert.Contains("PartitionKey ge 'newtonsoft'", filter);
        Assert.DoesNotContain("Newtonsoft", filter);
    }

    [Fact]
    public void TheVersionsFilterSelectsOnePartition()
    {
        var filter = TableSearchService.GenerateVersionsFilter("Newtonsoft.Json", includePrerelease: false, includeSemVer2: false);

        Assert.Contains("PartitionKey eq 'newtonsoft.json'", filter);
        Assert.Contains("Listed eq true", filter);
        Assert.Contains("IsPrerelease eq false", filter);
        Assert.Contains("SemVerLevel eq 0", filter);
    }
}
