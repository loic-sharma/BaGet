using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NuGet.Frameworks;

namespace BaGet.Core.Services
{
    public class FrameworkCompatibilityService : IFrameworkCompatibilityService
    {
        private static readonly Dictionary<string, NuGetFramework> KnownFrameworks;
        private static readonly IReadOnlyList<OneWayCompatibilityMappingEntry> CompatibilityMapping;
        private static readonly ConcurrentDictionary<NuGetFramework, IReadOnlyList<string>> CompatibleFrameworks;

        static FrameworkCompatibilityService()
        {
            var supportedFrameworks = new HashSet<string>();
            supportedFrameworks.Add(FrameworkConstants.FrameworkIdentifiers.NetStandard);
            supportedFrameworks.Add(FrameworkConstants.FrameworkIdentifiers.NetCoreApp);
            supportedFrameworks.Add(FrameworkConstants.FrameworkIdentifiers.Net);

            CompatibilityMapping = DefaultFrameworkMappings.Instance.CompatibilityMappings.ToList();
            CompatibleFrameworks = new ConcurrentDictionary<NuGetFramework, IReadOnlyList<string>>();

            KnownFrameworks = typeof(FrameworkConstants.CommonFrameworks)
                .GetFields()
                .Where(f => f.IsStatic)
                .Where(f => f.FieldType == typeof(NuGetFramework))
                .Select(f => (NuGetFramework)f.GetValue(null))
                .Where(f => supportedFrameworks.Contains(f.Framework))
                .ToDictionary(f => f.GetShortFolderName());
        }

        public IReadOnlyList<string> FindAllCompatibleFrameworks(string name)
        {
            if (!KnownFrameworks.TryGetValue(name, out var framework))
            {
                return new List<string> { name };
            }

            return CompatibleFrameworks.GetOrAdd(framework, FindAllCompatibleFrameworks);
        }

        private IReadOnlyList<string> FindAllCompatibleFrameworks(NuGetFramework targetFramework)
        {
            var results = new HashSet<string>();

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
                    if (mapping.SupportedFrameworkRange.Satisfies(possibleFramework))
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
                if (possibleFramework.Framework == targetFramework.Framework &&
                    possibleFramework.Version <= targetFramework.Version)
                {
                    results.Add(possibleFramework.GetShortFolderName());
                }
            }

            return results.ToList();
        }
    }
}
