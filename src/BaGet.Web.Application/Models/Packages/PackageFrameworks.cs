using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Frameworks;

namespace BaGet.Web.Application.Models.Packages;

/// <summary>
/// Groups a package's declared target frameworks and computes the further
/// frameworks it is compatible with, the way nuget.org's Frameworks tab does.
/// </summary>
internal static class PackageFrameworks
{
    public const string Net = ".NET";
    public const string NetCore = ".NET Core";
    public const string NetStandard = ".NET Standard";
    public const string NetFramework = ".NET Framework";
    public const string Other = "Other";

    private static readonly string[] GroupOrder = { Net, NetCore, NetStandard, NetFramework, Other };

    /// <summary>The current frameworks a package is checked against.</summary>
    internal static readonly IReadOnlyList<string> KnownFrameworks = new[]
    {
        "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0",
        "netcoreapp3.1", "netcoreapp3.0", "netcoreapp2.2", "netcoreapp2.1", "netcoreapp2.0",
        "netstandard2.1", "netstandard2.0",
        "net481", "net48", "net472", "net471", "net47", "net462", "net461", "net46", "net452", "net451", "net45",
    };

    public static IReadOnlyList<FrameworkGroup> Compute(IEnumerable<string?>? declaredMonikers)
    {
        #pragma warning disable CS8602
        var declared = (declaredMonikers ?? Enumerable.Empty<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => (Moniker: m.Trim(), Framework: NuGetFramework.ParseFolder(m.Trim())))
            .Where(f => !f.Framework.IsAny && !f.Framework.IsAgnostic)
            .GroupBy(f => f.Moniker, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        #pragma warning restore CS8602

        var groups = new Dictionary<string, FrameworkGroup>();

        FrameworkGroup GroupFor(string name)
        {
            if (!groups.TryGetValue(name, out var group))
            {
                groups[name] = group = new FrameworkGroup { Name = name };
            }

            return group;
        }

        foreach (var (moniker, framework) in declared)
        {
            GroupFor(GroupName(framework)).Declared.Add(moniker);
        }

        var declaredFrameworks = declared
            .Where(f => !f.Framework.IsUnsupported)
            .Select(f => f.Framework)
            .ToList();

        if (declaredFrameworks.Count > 0)
        {
            var reducer = new FrameworkReducer();

            foreach (var moniker in KnownFrameworks)
            {
                if (declared.Any(d => string.Equals(d.Moniker, moniker, StringComparison.OrdinalIgnoreCase))) continue;

                var target = NuGetFramework.ParseFolder(moniker);
                if (reducer.GetNearest(target, declaredFrameworks) != null)
                {
                    GroupFor(GroupName(target)).Computed.Add(moniker);
                }
            }
        }

        return GroupOrder
            .Where(groups.ContainsKey)
            .Select(name => groups[name])
            .ToList();
    }

    private static string GroupName(NuGetFramework framework)
    {
        if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetCoreApp, StringComparison.OrdinalIgnoreCase))
        {
            return framework.Version.Major >= 5 ? Net : NetCore;
        }

        if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetStandard, StringComparison.OrdinalIgnoreCase))
        {
            return NetStandard;
        }

        if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.Net, StringComparison.OrdinalIgnoreCase))
        {
            return NetFramework;
        }

        return Other;
    }
}
