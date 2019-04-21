using System;
using System.Collections.Generic;
using BaGet.Protocol.Converters;
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
        public SearchResult(
            string id,
            NuGetVersion version,
            string description,
            IReadOnlyList<string> authors,
            string iconUrl,
            string licenseUrl,
            string projectUrl,
            string registrationIndexUrl,
            string summary,
            IReadOnlyList<string> tags,
            string title,
            long totalDownloads,
            IReadOnlyList<SearchResultVersion> versions)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException(nameof(id));
            }

            version = version ?? throw new ArgumentNullException(nameof(version));
            versions = versions ?? throw new ArgumentNullException(nameof(versions));

            Id = id;
            Version = version;
            Description = description;
            Authors = authors;
            IconUrl = iconUrl;
            LicenseUrl = licenseUrl;
            ProjectUrl = projectUrl;
            RegistrationIndexUrl = registrationIndexUrl;
            Summary = summary;
            Tags = tags;
            Title = title;
            TotalDownloads = totalDownloads;

            Versions = versions;
        }

        public string Id { get; }

        [JsonConverter(typeof(NuGetVersionConverter), NuGetVersionConversionFlags.IncludeBuildMetadata)]
        public NuGetVersion Version { get; }

        public string Description { get; }

        [JsonConverter(typeof(SingleOrListConverter<string>))]
        public IReadOnlyList<string> Authors { get; }
        public string IconUrl { get; }
        public string LicenseUrl { get; }
        public string ProjectUrl { get; }

        [JsonProperty(PropertyName = "registration")]
        public string RegistrationIndexUrl { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Tags { get; }
        public string Title { get; }
        public long TotalDownloads { get; }

        public IReadOnlyList<SearchResultVersion> Versions { get; }
    }
}
