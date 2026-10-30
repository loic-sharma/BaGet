using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Xunit;

namespace BaGet.Azure.Tests.Table;

/// <summary>
/// The table's JSON columns moved from Newtonsoft.Json to System.Text.Json. Existing rows
/// were written by Newtonsoft, so they must still read, and new rows must look the same.
/// </summary>
public class TableJsonTests
{
    private static readonly List<DependencyModel> Dependencies = new()
    {
        new DependencyModel { Id = "Newtonsoft.Json", VersionRange = "[13.0.1, )", TargetFramework = "net6.0" },
        new DependencyModel { Id = null, VersionRange = null, TargetFramework = "netstandard2.0" },
    };

    [Fact]
    public void ReadsDependenciesNewtonsoftWrote()
    {
        var read = TableJson.Deserialize<List<DependencyModel>>(JsonConvert.SerializeObject(Dependencies));

        Assert.Equal(
            Dependencies.Select(d => (d.Id, d.VersionRange, d.TargetFramework)),
            read.Select(d => (d.Id, d.VersionRange, d.TargetFramework)));
    }

    [Fact]
    public void WritesWhatNewtonsoftWrote()
    {
        Assert.Equal(JsonConvert.SerializeObject(Dependencies), TableJson.Serialize(Dependencies));

        var types = new List<PackageTypeModel> { new() { Name = "Dependency", Version = "" } };
        Assert.Equal(JsonConvert.SerializeObject(types), TableJson.Serialize(types));
    }

    public static IEnumerable<object[]> StringArrays => new[]
    {
        new object[] { new string[0] },
        new object[] { new[] { "Ação", "日本語", "a\"b\\c" } },
    };

    [Theory]
    [MemberData(nameof(StringArrays))]
    public void StringArraysMatchNewtonsoft(string[] values)
    {
        var newtonsoft = JsonConvert.SerializeObject(values);

        Assert.Equal(newtonsoft, TableJson.Serialize(values));
        Assert.Equal(values, TableJson.Deserialize<string[]>(newtonsoft));
    }

    [Fact]
    public void ReadsPropertyNamesInAnyCase()
    {
        var read = TableJson.Deserialize<List<PackageTypeModel>>("[{\"name\":\"DotnetTool\",\"VERSION\":\"1.0\"}]");

        Assert.Equal("DotnetTool", read.Single().Name);
        Assert.Equal("1.0", read.Single().Version);
    }

    [Fact]
    public void ANullDocumentReadsAsNull()
    {
        Assert.Null(TableJson.Deserialize<string[]>("null"));
    }
}

public class TablePartitionCountTests
{
    [Fact]
    public void CountsEachRunOfAPartitionOnce()
    {
        Assert.Equal(3, TableSearchService.CountPartitions(new[] { "a", "a", "b", "c", "c", "c" }));
    }

    [Fact]
    public void NoRowsIsNoPartitions()
    {
        Assert.Equal(0, TableSearchService.CountPartitions(new string[0]));
    }

    [Fact]
    public void PartitionKeysAreComparedOrdinally()
    {
        // Partition keys are lowercased IDs; a differently cased key is a different partition.
        Assert.Equal(2, TableSearchService.CountPartitions(new[] { "a", "A" }));
    }
}