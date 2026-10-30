using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace BaGet.Web.Application.Models.Packages;

/// <summary>
/// A package's dependencies grouped by target framework, for display. Shared by the
/// package page and the upload preview, which must describe the same package the same
/// way: a preview that grouped or named frameworks differently would be a preview of
/// something else.
/// </summary>
public static class PackageDependencies
{
    public static IReadOnlyList<DependencyGroupModel> GroupByFramework(
        IEnumerable<PackageDependency>? dependencies)
    {
        if (dependencies == null) return Array.Empty<DependencyGroupModel>();

        return dependencies
            .GroupBy(d => d.TargetFramework)
            .Select(group => new DependencyGroupModel
            {
                Name = PrettifyTargetFramework(group.Key),
                Dependencies = group
                    .Where(d => d.Id != null)
                    .Select(d => new DependencyModel
                    {
                        PackageId = d.Id,
                        VersionSpec = (d.VersionRange != null)
                            ? VersionRange.Parse(d.VersionRange).PrettyPrint()
                            : string.Empty
                    })
                    .ToList()
            })
            .ToList();
    }

    public static string PrettifyTargetFramework(string? targetFramework)
    {
        if (targetFramework == null) return "All Frameworks";

        NuGetFramework framework;
        try
        {
            framework = NuGetFramework.Parse(targetFramework);
        }
        catch (Exception)
        {
            return targetFramework;
        }

        string frameworkName;
        if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetCoreApp,
                StringComparison.OrdinalIgnoreCase))
        {
            frameworkName = (framework.Version.Major >= 5)
                ? ".NET"
                : ".NET Core";
        }
        else if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetStandard,
                     StringComparison.OrdinalIgnoreCase))
        {
            frameworkName = ".NET Standard";
        }
        else if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.Net,
                     StringComparison.OrdinalIgnoreCase))
        {
            frameworkName = ".NET Framework";
        }
        else
        {
            frameworkName = framework.Framework;
        }

        var frameworkVersion = (framework.Version.Build == 0)
            ? framework.Version.ToString(2)
            : framework.Version.ToString(3);

        return $"{frameworkName} {frameworkVersion}";
    }
}
