using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Controllers
{
    public class SearchController
    {
        public object Get()
        {
            return new
            {
                TotalHits = 1,
                Data = new[]
                {
                    new SearchResult("Newtonsoft.Json", "9.0.1", new List<string> { "3.0.8", "9.0.1"}),
                }
            };
        }

        private class SearchResult
        {
            public SearchResult(string packageId, string version, IReadOnlyList<string> versions)
            {
                if (string.IsNullOrEmpty(packageId)) throw new ArgumentNullException(nameof(packageId));
                if (string.IsNullOrEmpty(version)) throw new ArgumentNullException(nameof(version));

                PackageId = packageId;
                Version = version;
                Versions = versions ?? throw new ArgumentNullException(nameof(versions));
            }

            [JsonProperty(PropertyName = "id")]
            public string PackageId { get; }

            public string Version { get; }

            public IReadOnlyList<string> Versions { get; }
        }
    }
}