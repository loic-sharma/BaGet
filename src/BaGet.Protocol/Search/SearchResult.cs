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
        [JsonProperty(PropertyName = "id")]
        public string PackageId { get; set; }

        [JsonConverter(typeof(NuGetVersionConverter), NuGetVersionConversionFlags.IncludeBuildMetadata)]
        public NuGetVersion Version { get; set; }

        public string Description { get; set; }

        [JsonConverter(typeof(SingleOrListConverter<string>))]
        public IReadOnlyList<string> Authors { get; set; }
        public string IconUrl { get; set; }
        public string LicenseUrl { get; set; }
        public string ProjectUrl { get; set; }

        [JsonProperty(PropertyName = "registration")]
        public string RegistrationIndexUrl { get; set; }
        public string Summary { get; set; }
        public IReadOnlyList<string> Tags { get; set; }
        public string Title { get; set; }
        public long TotalDownloads { get; set; }

        public IReadOnlyList<SearchResultVersion> Versions { get; set; }
    }
}
