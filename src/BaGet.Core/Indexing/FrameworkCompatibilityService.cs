using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NuGet.Frameworks;

namespace BaGet.Core;

using static NuGet.Frameworks.FrameworkConstants;

public class FrameworkCompatibilityService : IFrameworkCompatibilityService
{
    private const string AnyFramework = "any";

    private static readonly Dictionary<string, NuGetFramework?> KnownFrameworks;
    private static readonly IReadOnlyList<OneWayCompatibilityMappingEntry> CompatibilityMapping;
    private static readonly ConcurrentDictionary<NuGetFramework, IReadOnlyList<string>> CompatibleFrameworks;

    static FrameworkCompatibilityService()
    {
        var supportedFrameworks = new HashSet<string?>();
        supportedFrameworks.Add(FrameworkIdentifiers.NetStandard);
        supportedFrameworks.Add(FrameworkIdentifiers.NetCoreApp);
        supportedFrameworks.Add(FrameworkIdentifiers.Net);

        CompatibilityMapping = DefaultFrameworkMappings.Instance.CompatibilityMappings.ToList();
        CompatibleFrameworks = new ConcurrentDictionary<NuGetFramework, IReadOnlyList<string>>();

        #pragma warning disable CS8619, CS8621, CS8714
        KnownFrameworks = typeof(CommonFrameworks)
            .GetFields()
            .Where(f => f.IsStatic)
            .Where(f => f.FieldType == typeof(NuGetFramework))
            .Select(f => (NuGetFramework?)f.GetValue(null))
            .Where(f => supportedFrameworks.Contains(f?.Framework))
            .ToDictionary(f => f?.GetShortFolderName());
        #pragma warning restore CS8619, CS8621, CS8714

        // Add more frameworks missing from "CommonFrameworks"
        KnownFrameworks["net472"] = new NuGetFramework(FrameworkIdentifiers.Net, new Version(4, 7, 2, 0));
        KnownFrameworks["net471"] = new NuGetFramework(FrameworkIdentifiers.Net, new Version(4, 7, 1, 0));
    }

    public IReadOnlyList<string> FindAllCompatibleFrameworks(string name)
    {
        if (!KnownFrameworks.TryGetValue(name, out var framework))
        {
            return new List<string> { name, AnyFramework };
        }

        #pragma warning disable CS8604, CS8621
        return CompatibleFrameworks.GetOrAdd(framework, FindAllCompatibleFrameworks);
        #pragma warning restore CS8604, CS8621
    }

    private IReadOnlyList<string?> FindAllCompatibleFrameworks(NuGetFramework targetFramework)
    {
        var results = new HashSet<string?> { AnyFramework };

        // Find all framework mappings that apply to the target framework
        foreach (var mapping in CompatibilityMapping)
        {
            // Skip this mapping if it isn't for our target framework.
            if (!mapping.TargetFrameworkRange.Satisfies(targetFramework))
            {
                continue;
            }

            // Any framework that satisfies this mapping is compatible with the target framework.
            foreach (var possibleFramework in KnownFrameworks.Values)
            {
                #pragma warning disable CS8604
                if (mapping.SupportedFrameworkRange.Satisfies(possibleFramework))
                #pragma warning restore CS8604
                {
                    results.Add(possibleFramework.GetShortFolderName());
                }
            }
        }

        // Find all frameworks that have the same "framework identifier" and a version
        // less than equal to our target framework. For example, "net45" is compatible
        // with "net20" and "net45".
        foreach (var possibleFramework in KnownFrameworks.Values)
        {
            if (possibleFramework?.Framework == targetFramework.Framework &&
                possibleFramework?.Version <= targetFramework.Version)
            {
                results.Add(possibleFramework?.GetShortFolderName());
            }
        }

        return results.ToList();
    }
}