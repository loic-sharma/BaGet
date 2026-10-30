using System.Collections.Generic;

namespace BaGet.Web.Application.Snippets;

/// <summary>
/// The "install this package" commands of the package page, one tab per tool.
/// </summary>
internal static class InstallSnippets
{
    public static IReadOnlyList<InstallSnippet> Build(string? id, string? version, bool isTemplate, bool isTool)
    {
        if (isTemplate)
        {
            return new[]
            {
                new InstallSnippet { Name = ".NET CLI", Prefix = ">", Lines = new[] { $"dotnet new install {id}::{version}" } },
            };
        }

        if (isTool)
        {
            return new[]
            {
                new InstallSnippet { Name = ".NET CLI", Prefix = ">", Lines = new[] { $"dotnet tool install --global {id} --version {version}" } },
            };
        }

        // Cake needs an explicit opt-in to resolve a prerelease version.
        #pragma warning disable CS8602
        var cakeSuffix = version.Contains('-') ? "&prerelease" : string.Empty;
        #pragma warning restore CS8602
        var cakeSource = $"nuget:?package={id}&version={version}{cakeSuffix}";

        return new[]
        {
            new InstallSnippet { Name = ".NET CLI", Prefix = ">", Lines = new[] { $"dotnet add package {id} --version {version}" } },
            new InstallSnippet { Name = "PackageReference", Prefix = "", Lines = new[] { $"<PackageReference Include=\"{id}\" Version=\"{version}\" />" } },
            new InstallSnippet
            {
                Name = "Central Package Management",
                Prefix = "",
                Lines = new[] { $"<PackageVersion Include=\"{id}\" Version=\"{version}\" />", $"<PackageReference Include=\"{id}\" />" },
            },
            new InstallSnippet { Name = "Paket CLI", Prefix = ">", Lines = new[] { $"paket add {id} --version {version}" } },
            new InstallSnippet { Name = "Package Manager", Prefix = "PM>", Lines = new[] { $"Install-Package {id} -Version {version}" } },
            new InstallSnippet { Name = "Script & Interactive", Prefix = "", Lines = new[] { $"#r \"nuget: {id}, {version}\"" } },
            new InstallSnippet { Name = "File-based apps", Prefix = "", Lines = new[] { $"#:package {id}@{version}" } },
            new InstallSnippet { Name = "Cake", Prefix = "", Lines = new[] { $"#addin {cakeSource}", $"#tool {cakeSource}" } },
            new InstallSnippet { Name = "NUKE", Prefix = ">", Lines = new[] { $"nuke :add-package {id} --version {version}" } },
        };
    }
}
