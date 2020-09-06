using System.Collections.Generic;
using System.Text.Json.Serialization;

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
        [JsonPropertyName("targetFramework")]
        public string TargetFramework { get; set; }

        /// <summary>
        /// A list of dependencies.
        /// </summary>
        [JsonPropertyName("dependencies")]
        public List<DependencyItem> Dependencies { get; set; }
    }
}
