using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BaGet.Core.Tests.Repositories;

public class VirtualRepositorySettingsTests
{
    private static Repository Row(string name, RepositoryKind kind, int key, VirtualRepositorySettings? settings = null, RepositoryFormat format = RepositoryFormat.NuGet)
        => new()
        {
            Key = key,
            Name = name,
            Kind = kind,
            Format = format,
            Settings = settings == null ? null : VirtualSettingsCodec.Serialize(settings),
        };

    private static Dictionary<string, Repository> Rows(params Repository[] rows) => rows.ToDictionary(r => r.Name!);

    private static VirtualRepositorySettings Settings(string? deploy, params string[] members)
        => new() { Members = members.ToList(), DeployRepository = deploy };

    [Fact]
    public void SettingsRoundTripAndAnEmptyOrBrokenValueReadsAsNoMembers()
    {
        var json = VirtualSettingsCodec.Serialize(Settings("b", "a", "b"));
        var read = VirtualSettingsCodec.Deserialize(json);

        Assert.Equal(new[] { "a", "b" }, read.Members);
        Assert.Equal("b", read.DeployRepository);

        foreach (var value in new[] { null, "", "  ", "{not json", "[]", "{\"members\":null}" })
        {
            var empty = VirtualSettingsCodec.Deserialize(value);
            Assert.Empty(empty.Members);
            Assert.Null(empty.DeployRepository);
        }
    }

    [Fact]
    public void ShapeValidationListsEveryProblem()
    {
        Assert.Empty(Settings("a", "a", "b").ValidateShape());
        Assert.Empty(Settings(null).ValidateShape());

        var problems = Settings("c", "a", "a", "Bad Name").ValidateShape();
        Assert.Contains(problems, p => p.Contains("'a' is listed twice"));
        Assert.Contains(problems, p => p.Contains("'Bad Name' is not a valid repository name"));
        Assert.Contains(problems, p => p.Contains("deploy repository 'c' must be one of the members"));

        var many = new VirtualRepositorySettings { Members = Enumerable.Range(0, VirtualRepositorySettings.MaxMembers + 1).Select(i => "m" + i).ToList() };
        Assert.Contains(many.ValidateShape(), p => p.Contains("at most"));
    }

    [Fact]
    public void MembersAreFlattenedInOrderWithEachRepositoryOnceAndNestedVirtualsExpandedInPlace()
    {
        var rows = Rows(
            Row("top", RepositoryKind.Virtual, 10, Settings(null, "a", "inner", "c", "a")),
            Row("inner", RepositoryKind.Virtual, 11, Settings(null, "b", "a", "remote")),
            Row("a", RepositoryKind.Local, 1),
            Row("b", RepositoryKind.Local, 2),
            Row("c", RepositoryKind.Local, 3),
            Row("remote", RepositoryKind.Remote, 4));

        var result = new List<RepositoryInfo>();
        VirtualRepositories.Flatten("top", rows, new HashSet<string>(), result, new HashSet<int>(), 0);

        Assert.Equal(new[] { "a", "b", "remote", "c" }, result.Select(r => r.Name));
    }

    [Fact]
    public void ACycleIsCutWhereItClosesAndMissingOrForeignMembersAreSkipped()
    {
        var rows = Rows(
            Row("x", RepositoryKind.Virtual, 10, Settings(null, "a", "y")),
            Row("y", RepositoryKind.Virtual, 11, Settings(null, "x", "b", "ghost", "maven")),
            Row("a", RepositoryKind.Local, 1),
            Row("b", RepositoryKind.Local, 2),
            Row("maven", RepositoryKind.Local, 3, format: (RepositoryFormat)1));

        var result = new List<RepositoryInfo>();
        VirtualRepositories.Flatten("x", rows, new HashSet<string>(), result, new HashSet<int>(), 0);

        Assert.Equal(new[] { "a", "b" }, result.Select(r => r.Name));
    }

    [Fact]
    public void TheDeployTargetFollowsVirtualDeployRepositoriesToALocalOne()
    {
        var rows = Rows(
            Row("outer", RepositoryKind.Virtual, 10, Settings("inner", "inner")),
            Row("inner", RepositoryKind.Virtual, 11, Settings("a", "a", "remote")),
            Row("a", RepositoryKind.Local, 1),
            Row("remote", RepositoryKind.Remote, 2),
            Row("toremote", RepositoryKind.Virtual, 12, Settings("remote", "remote")),
            Row("none", RepositoryKind.Virtual, 13, Settings(null, "a")),
            Row("loop1", RepositoryKind.Virtual, 14, Settings("loop2", "loop2")),
            Row("loop2", RepositoryKind.Virtual, 15, Settings("loop1", "loop1")));

        Assert.Equal("a", VirtualRepositories.ResolveDeploy("outer", rows)!.Name);
        Assert.Equal("a", VirtualRepositories.ResolveDeploy("inner", rows)!.Name);
        Assert.Null(VirtualRepositories.ResolveDeploy("toremote", rows));
        Assert.Null(VirtualRepositories.ResolveDeploy("none", rows));
        Assert.Null(VirtualRepositories.ResolveDeploy("loop1", rows));
        Assert.Null(VirtualRepositories.ResolveDeploy("a", rows));
        Assert.Null(VirtualRepositories.ResolveDeploy("ghost", rows));
    }

    [Fact]
    public void PackageListsFromSeveralMembersMergeByVersionAndTheFirstListWins()
    {
        var one = new Package { Id = "P", Version = NuGet.Versioning.NuGetVersion.Parse("1.0.0"), Description = "first" };
        var other = new Package { Id = "P", Version = NuGet.Versioning.NuGetVersion.Parse("1.0.0"), Description = "second" };
        var extra = new Package { Id = "P", Version = NuGet.Versioning.NuGetVersion.Parse("2.0.0"), Description = "third" };

        var union = VirtualRepositoryReader.Union(new IReadOnlyList<Package>[] { new[] { one }, new[] { other, extra } });

        Assert.Equal(2, union.Count);
        Assert.Equal("first", union.Single(p => p.Version.ToNormalizedString() == "1.0.0").Description);
    }
}
