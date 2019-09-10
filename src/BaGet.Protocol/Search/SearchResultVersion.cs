using System;
using BaGet.Protocol.Internal;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// The version of a package that matched a search query.
    /// See: <see cref="SearchResult"/>.
    /// Documentation: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#search-result
    /// </summary>
    public class SearchResultVersion
    {
        [JsonProperty(PropertyName = "@id")]
        public string RegistrationLeafUrl { get; set; }

        [JsonConverter(typeof(NuGetVersionConverter), NuGetVersionConversionFlags.IncludeBuildMetadata)]
        public NuGetVersion Version { get; set; }

        public long Downloads { get; set; }
    }
}
