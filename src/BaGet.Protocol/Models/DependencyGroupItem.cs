using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The dependencies of the package for a specific target framework.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#package-dependency-group
    /// </summary>
    public class DependencyGroupItem
    {
        /// <summary>
        /// The target framework that these dependencies are applicable to.
        /// </summary>
        [JsonProperty("targetFramework")]
        public string TargetFramework { get; set; }

        /// <summary>
        /// A list of dependencies.
        /// </summary>
        [JsonProperty("dependencies", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<DependencyItem> Dependencies { get; set; }
    }
}
