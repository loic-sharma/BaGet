using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    public class CatalogPackageDependencyGroup
    {
        [JsonProperty("targetFramework")]
        public string TargetFramework { get; set; }

        [JsonProperty("dependencies")]
        public List<CatalogPackageDependency> Dependencies { get; set; }
    }
}
