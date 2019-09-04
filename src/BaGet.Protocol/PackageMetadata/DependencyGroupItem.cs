using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// The dependencies of the package for a specific target framework.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#package-dependency-group
    /// </summary>
    public class DependencyGroupItem
    {
        [JsonProperty("targetFramework")]
        public string TargetFramework { get; set; }

        [JsonProperty("dependencies", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<DependencyItem> Dependencies { get; set; }
    }
}
