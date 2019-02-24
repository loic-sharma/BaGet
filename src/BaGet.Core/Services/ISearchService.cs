using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using NuGet.Versioning;

namespace BaGet.Core.Services
{
    public interface ISearchService
    {
        Task IndexAsync(Package package);

        Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            string framework = null);

        Task<IReadOnlyList<string>> AutocompleteAsync(string query, int skip = 0, int take = 20);

        Task<IReadOnlyList<string>> FindDependentsAsync(string packageId, int skip = 0, int take = 20);
    }

    public class SearchResult
    {
        public string Id { get; set; }

        public NuGetVersion Version { get; set; }

        public string Description { get; set; }
        public IReadOnlyList<string> Authors { get; set; }
        public string IconUrl { get; set; }
        public string LicenseUrl { get; set; }
        public string ProjectUrl { get; set; }
        public string Summary { get; set; }
        public string[] Tags { get; set; }
        public string Title { get; set; }
        public long TotalDownloads { get; set; }

        public IReadOnlyList<SearchResultVersion> Versions { get; set; }
    }

    public class SearchResultVersion
    {
        public SearchResultVersion(NuGetVersion version, long downloads)
        {
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Downloads = downloads;
        }

        public NuGetVersion Version { get; }

        public long Downloads { get; }
    }
}
