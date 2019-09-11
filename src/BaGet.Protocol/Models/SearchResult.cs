using System;
using System.Collections.Generic;
using BaGet.Protocol.Internal;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// A package that matched a search query.
    /// Documentation: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#search-result
    /// </summary>
    public class SearchResult
    {
        [JsonProperty("id")]
        public string PackageId { get; set; }

        [JsonProperty("version")]
        [JsonConverter(typeof(NuGetVersionConverter), NuGetVersionConversionFlags.IncludeBuildMetadata)]
        public NuGetVersion Version { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("authors")]
        [JsonConverter(typeof(SingleOrListConverter<string>))]
        public IReadOnlyList<string> Authors { get; set; }

        [JsonProperty("iconUrl")]
        public string IconUrl { get; set; }

        [JsonProperty("licenseUrl")]
        public string LicenseUrl { get; set; }

        [JsonProperty("projectUrl")]
        public string ProjectUrl { get; set; }

        [JsonProperty("registration")]
        public string RegistrationIndexUrl { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("tags")]
        public IReadOnlyList<string> Tags { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("totalDownloads")]
        public long TotalDownloads { get; set; }

        [JsonProperty("versions")]
        public IReadOnlyList<SearchResultVersion> Versions { get; set; }
    }
}
