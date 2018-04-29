using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;

namespace BaGet.Azure.Search
{
    // See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#search-for-packages
    [SerializePropertyNamesAsCamelCase]
    public class PackageDocument
    {
        public const string IndexName = "packages";

        [Key]
        public string Key { get; set; }

        [IsSearchable, IsFilterable, IsSortable]
        public string Id { get; set; }

        [IsSearchable, IsFilterable, IsSortable]
        public string Version { get; set; }

        [IsSearchable]
        public string Description { get; set; }
        public string[] Authors { get; set; }
        public string IconUrl { get; set; }
        public string LicenseUrl { get; set; }
        public string ProjectUrl { get; set; }

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

        public string[] Versions { get; set;  }
        public string[] VersionDownloads { get; set; }
    }
}
