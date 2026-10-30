using System.Linq;
using Xunit;
using BaGet.Web.Application.Models.Packages;

namespace BaGet.Tests;

public class PackageFrameworksTests
{
    private static FrameworkGroup? Group(System.Collections.Generic.IReadOnlyList<FrameworkGroup> groups, string name)
        => groups.SingleOrDefault(g => g.Name == name);

    [Fact]
    public void DeclaredFrameworksAreGroupedByFamily()
    {
        var groups = PackageFrameworks.Compute(new[] { "net8.0", "net5.0", "netcoreapp3.1", "netstandard2.0", "net48", "uap10.0" });

        #pragma warning disable CS8602
        Assert.Equal(new[] { "net5.0", "net8.0" }, Group(groups, ".NET").Declared.OrderBy(m => m));
        Assert.Equal(new[] { "netcoreapp3.1" }, Group(groups, ".NET Core").Declared);
        Assert.Equal(new[] { "netstandard2.0" }, Group(groups, ".NET Standard").Declared);
        Assert.Equal(new[] { "net48" }, Group(groups, ".NET Framework").Declared);
        Assert.Equal(new[] { "uap10.0" }, Group(groups, "Other").Declared);
        #pragma warning restore CS8602
    }

    [Fact]
    public void GroupsComeInNugetOrgOrder()
    {
        var groups = PackageFrameworks.Compute(new[] { "net48", "uap10.0", "netstandard2.0", "net8.0", "netcoreapp3.1" });

        Assert.Equal(new[] { ".NET", ".NET Core", ".NET Standard", ".NET Framework", "Other" }, groups.Select(g => g.Name));
    }

    [Fact]
    public void ANetStandard20PackageIsComputedCompatibleWithNewerFrameworks()
    {
        var groups = PackageFrameworks.Compute(new[] { "netstandard2.0" });

        var net = Group(groups, ".NET");
        #pragma warning disable CS8602
        Assert.Empty(net.Declared);
        #pragma warning restore CS8602
        Assert.Contains("net10.0", net.Computed);
        Assert.Contains("net5.0", net.Computed);

        #pragma warning disable CS8602
        Assert.Contains("netcoreapp3.1", Group(groups, ".NET Core").Computed);
        Assert.Contains("net472", Group(groups, ".NET Framework").Computed);
        Assert.Contains("net461", Group(groups, ".NET Framework").Computed);
        Assert.DoesNotContain("net45", Group(groups, ".NET Framework").Computed);
        #pragma warning restore CS8602

        // netstandard2.1 is newer than the declared netstandard2.0, so a 2.0 package fits it.
        #pragma warning disable CS8602
        Assert.Contains("netstandard2.1", Group(groups, ".NET Standard").Computed);
        Assert.DoesNotContain("netstandard2.0", Group(groups, ".NET Standard").Computed);
        #pragma warning restore CS8602
    }

    [Fact]
    public void ADeclaredFrameworkIsNotAlsoComputed()
    {
        var groups = PackageFrameworks.Compute(new[] { "net8.0" });

        var net = Group(groups, ".NET");
        #pragma warning disable CS8602
        Assert.Equal(new[] { "net8.0" }, net.Declared);
        #pragma warning restore CS8602
        Assert.DoesNotContain("net8.0", net.Computed);
        Assert.Contains("net9.0", net.Computed);
        Assert.Contains("net10.0", net.Computed);
        Assert.DoesNotContain("net7.0", net.Computed);
    }

    [Fact]
    public void ANet5PackageIsNotComputedCompatibleWithOldFrameworks()
    {
        var groups = PackageFrameworks.Compute(new[] { "net5.0" });

        Assert.Null(Group(groups, ".NET Framework"));
        Assert.Null(Group(groups, ".NET Standard"));
        Assert.Null(Group(groups, ".NET Core"));
    }

    [Fact]
    public void APlatformSpecificFrameworkDoesNotMakeThePlainOneCompatible()
    {
        var groups = PackageFrameworks.Compute(new[] { "net8.0-windows7.0" });

        var net = Group(groups, ".NET");
        #pragma warning disable CS8602
        Assert.Equal(new[] { "net8.0-windows7.0" }, net.Declared);
        #pragma warning restore CS8602
        Assert.DoesNotContain("net8.0", net.Computed);
    }

    [Fact]
    public void NoDeclaredFrameworksGivesNoGroups()
    {
        Assert.Empty(PackageFrameworks.Compute(new string[0]));
        Assert.Empty(PackageFrameworks.Compute(null));
    }

    [Fact]
    public void AgnosticAndBlankMonikersAreIgnored()
    {
        Assert.Empty(PackageFrameworks.Compute(new[] { "any", "", null }));
    }

    [Fact]
    public void AnUnrecognisedMonikerIsListedUnderOtherAndComputesNothing()
    {
        var groups = PackageFrameworks.Compute(new[] { "totallynew1.0" });

        var other = Assert.Single(groups);
        Assert.Equal("Other", other.Name);
        Assert.Empty(other.Computed);
    }
}
