using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;

namespace BaGet.Azure
{
    // See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#search-for-packages
    [SerializePropertyNamesAsCamelCase]
    public class PackageDocument : KeyedDocument
    {
        public const string IndexName = "packages";

        [IsSearchable, IsFilterable, IsSortable]
        public string Id { get; set; }

        /// <summary>
        /// The package's full versions after normalization, including any SemVer 2.0.0 build metadata.
        /// </summary>
        [IsSearchable, IsFilterable, IsSortable]
        public string Version { get; set; }

        [IsSearchable]
        public string Description { get; set; }
        public string[] Authors { get; set; }
        public bool HasEmbeddedIcon { get; set; }
        public string IconUrl { get; set; }
        public string LicenseUrl { get; set; }
        public string ProjectUrl { get; set; }
        public DateTimeOffset Published { get; set; }

        [IsSearchable]
        public string Summary { get; set; }

        [IsSearchable, IsFilterable, IsFacetable]
        public string[] Tags { get; set; }

        [IsSearchable]
        public string Title { get; set; }

        [IsFilterable, IsSortable]
        public long TotalDownloads { get; set; }

        [IsFilterable, IsSortable]
        public int DownloadsMagnitude { get; set; }

        /// <summary>
        /// The package's full versions after normalization, including any SemVer 2.0.0 build metadata.
        /// </summary>
        public string[] Versions { get; set;  }
        public string[] VersionDownloads { get; set; }

        [IsSearchable]
        [Analyzer(ExactMatchCustomAnalyzer.Name)]
        public string[] Dependencies { get; set; }

        [IsSearchable]
        [Analyzer(ExactMatchCustomAnalyzer.Name)]
        public string[] PackageTypes { get; set; }

        [IsSearchable]
        [Analyzer(ExactMatchCustomAnalyzer.Name)]
        public string[] Frameworks { get; set; }

        [IsFilterable]
        public string SearchFilters { get; set; }
    }

    [SerializePropertyNamesAsCamelCase]
    public class KeyedDocument : IKeyedDocument
    {
        [Key]
        public string Key { get; set; }
    }

    public interface IKeyedDocument
    {
        string Key { get; set; }
    }
}
