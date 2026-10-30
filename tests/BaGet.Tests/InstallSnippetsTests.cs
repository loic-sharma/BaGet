using System.Linq;
using Xunit;
using BaGet.Web.Application.Snippets;

namespace BaGet.Tests;

public class InstallSnippetsTests
{
    #pragma warning disable CS8604
    private static string[] Lines(string name, string? id = "My.Pkg", string? version = "1.2.3")
        => InstallSnippets.Build(id, version, isTemplate: false, isTool: false).Single(s => s.Name == name).Lines.ToArray();
    #pragma warning restore CS8604

    [Fact]
    public void TabsAreInTheDocumentedOrder()
    {
        var names = InstallSnippets.Build("My.Pkg", "1.2.3", false, false).Select(s => s.Name);

        Assert.Equal(new[]
        {
            ".NET CLI", "PackageReference", "Central Package Management", "Paket CLI", "Package Manager",
            "Script & Interactive", "File-based apps", "Cake", "NUKE",
        }, names);
    }

    [Fact]
    public void ExistingTabsAreUnchanged()
    {
        Assert.Equal(new[] { "dotnet add package My.Pkg --version 1.2.3" }, Lines(".NET CLI"));
        Assert.Equal(new[] { "<PackageReference Include=\"My.Pkg\" Version=\"1.2.3\" />" }, Lines("PackageReference"));
        Assert.Equal(new[] { "paket add My.Pkg --version 1.2.3" }, Lines("Paket CLI"));
        Assert.Equal(new[] { "Install-Package My.Pkg -Version 1.2.3" }, Lines("Package Manager"));
    }

    [Fact]
    public void ScriptAndInteractive()
        => Assert.Equal(new[] { "#r \"nuget: My.Pkg, 1.2.3\"" }, Lines("Script & Interactive"));

    [Fact]
    public void FileBasedApps()
        => Assert.Equal(new[] { "#:package My.Pkg@1.2.3" }, Lines("File-based apps"));

    [Fact]
    public void Cake()
        => Assert.Equal(new[]
        {
            "#addin nuget:?package=My.Pkg&version=1.2.3",
            "#tool nuget:?package=My.Pkg&version=1.2.3",
        }, Lines("Cake"));

    [Fact]
    public void Nuke()
        => Assert.Equal(new[] { "nuke :add-package My.Pkg --version 1.2.3" }, Lines("NUKE"));

    [Fact]
    public void CentralPackageManagement()
        => Assert.Equal(new[]
        {
            "<PackageVersion Include=\"My.Pkg\" Version=\"1.2.3\" />",
            "<PackageReference Include=\"My.Pkg\" />",
        }, Lines("Central Package Management"));

    [Fact]
    public void PrereleaseVersionsAreVerbatimAndCakeAllowsPrerelease()
    {
        var v = "2.0.0-beta.1+build5";
        Assert.Equal(new[] { "#r \"nuget: My.Pkg, 2.0.0-beta.1+build5\"" }, Lines("Script & Interactive", version: v));
        Assert.Equal(new[] { "#:package My.Pkg@2.0.0-beta.1+build5" }, Lines("File-based apps", version: v));
        Assert.Equal(new[]
        {
            "#addin nuget:?package=My.Pkg&version=2.0.0-beta.1+build5&prerelease",
            "#tool nuget:?package=My.Pkg&version=2.0.0-beta.1+build5&prerelease",
        }, Lines("Cake", version: v));
        Assert.Equal(new[] { "nuke :add-package My.Pkg --version 2.0.0-beta.1+build5" }, Lines("NUKE", version: v));
    }

    [Fact]
    public void TemplatesAndToolsKeepTheirSingleCommand()
    {
        var template = InstallSnippets.Build("My.Tpl", "1.0.0", isTemplate: true, isTool: false);
        var tool = InstallSnippets.Build("My.Tool", "1.0.0", isTemplate: false, isTool: true);

        Assert.Equal(new[] { "dotnet new install My.Tpl::1.0.0" }, Assert.Single(template).Lines);
        Assert.Equal(new[] { "dotnet tool install --global My.Tool --version 1.0.0" }, Assert.Single(tool).Lines);
    }
}
