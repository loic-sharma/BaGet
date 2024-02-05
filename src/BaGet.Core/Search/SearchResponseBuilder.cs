using BaGet.Protocol.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BaGet.Core
{
    public class SearchResponseBuilder : ISearchResponseBuilder
    {
        private readonly IUrlGenerator _url;

        public SearchResponseBuilder(IUrlGenerator url)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
        }

        public SearchResponse BuildSearch(IReadOnlyList<PackageRegistration> packageRegistrations)
        {
            var result = new List<SearchResult>();

            foreach (var packageRegistration in packageRegistrations)
            {
                var versions = packageRegistration.Packages.OrderByDescending(p => p.Version).ToList();
                var latest = versions.First();
                var iconUrl = latest.HasEmbeddedIcon
                    ? _url.GetPackageIconDownloadUrl(latest.Id, latest.Version)
                    : latest.IconUrlString;
                var licenseUrl = latest.HasEmbeddedLicense
                    ? _url.GetPackageLicenseDownloadUrl(latest.Id, latest.Version, latest.LicenseIsMarkDown)
                    : latest.LicenseUrlString;

                result.Add(new SearchResult
                {
                    PackageId = latest.Id,
                    Version = latest.Version.ToFullString(),
                    Description = latest.Description,
                    Authors = latest.Authors,
                    IconUrl = iconUrl,
                    LicenseUrl = licenseUrl,
                    ProjectUrl = latest.ProjectUrlString,
                    RegistrationIndexUrl = _url.GetRegistrationIndexUrl(latest.Id),
                    Summary = latest.Summary,
                    Tags = latest.Tags,
                    Title = latest.Title,
                    TotalDownloads = versions.Sum(p => p.Downloads),
                    Versions = versions
                        .Select(p => new SearchResultVersion
                        {
                            RegistrationLeafUrl = _url.GetRegistrationLeafUrl(p.Id, p.Version),
                            Version = p.Version.ToFullString(),
                            Downloads = p.Downloads,
                        })
                        .ToList(),
                });
            }

            return new SearchResponse
            {
                TotalHits = result.Count,
                Data = result,
                Context = SearchContext.Default(_url.GetPackageMetadataResourceUrl()),
            };
        }

        public AutocompleteResponse BuildAutocomplete(IReadOnlyList<string> data)
        {
            return new AutocompleteResponse
            {
                TotalHits = data.Count,
                Data = data,
                Context = AutocompleteContext.Default
            };
        }

        public DependentsResponse BuildDependents(IReadOnlyList<PackageDependent> packages)
        {
            return new DependentsResponse
            {
                TotalHits = packages.Count,
                Data = packages,
            };
        }
    }
}
