using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BaGet.Core;
using Microsoft.Azure.Search.Models;
using NuGet.Versioning;

namespace BaGet.Azure
{
    public class IndexActionBuilder
    {
        public virtual IReadOnlyList<IndexAction<KeyedDocument>> AddPackage(
            PackageRegistration registration)
        {
            var encodedId = EncodePackageId(registration.PackageId.ToLowerInvariant());
            var result = new List<IndexAction<KeyedDocument>>();

            foreach (var (searchFilters, packages) in PrepareFilters(registration))
            {
                if (!packages.Any()) continue;

                var documentKey = $"{encodedId}-{searchFilters}";
                var action = IndexAction.Upload<KeyedDocument>(
                    BuildFullDocument(documentKey, searchFilters, packages));

                result.Add(action);
            }

            return result;
        }

        public virtual IReadOnlyList<IndexAction<KeyedDocument>> UpdatePackage(
            PackageRegistration registration)
        {
            var encodedId = EncodePackageId(registration.PackageId.ToLowerInvariant());
            var result = new List<IndexAction<KeyedDocument>>();

            foreach (var (searchFilters, packages) in PrepareFilters(registration))
            {
                var documentKey = $"{encodedId}-{searchFilters}";
                IndexAction<KeyedDocument> action;

                if (!packages.Any())
                {
                    action = IndexAction.Delete(
                        new KeyedDocument
                        {
                            Key = documentKey
                        });
                }
                else
                {
                    action = IndexAction.MergeOrUpload<KeyedDocument>(
                        BuildFullDocument(documentKey, searchFilters, packages));
                }

                result.Add(action);
            }

            return result;
        }

        public virtual IReadOnlyList<IndexAction<KeyedDocument>> UpdateDownloads(
            PackageRegistration registration)
        {
            var encodedId = EncodePackageId(registration.PackageId.ToLowerInvariant());
            var result = new List<IndexAction<KeyedDocument>>();

            foreach (var (searchFilters, packages) in PrepareFilters(registration))
            {
                if (!packages.Any()) continue;

                var documentKey = $"{encodedId}-{searchFilters}";
                var action = IndexAction.Merge<KeyedDocument>(
                    BuildDownloadsDocument(documentKey, packages));

                result.Add(action);
            }

            return result;
        }

        private IEnumerable<(SearchFilters, List<Package>)> PrepareFilters(
            PackageRegistration registration)
        {
            for (var i = 0; i < 4; i++)
            {
                var includePrerelease = (i & 1) != 0;
                var includeSemVer2 = (i & 2) != 0;
                var searchFilters = (SearchFilters)i;

                var filtered = registration.Packages.Where(p => p.Listed);

                if (!includePrerelease)
                {
                    filtered = filtered.Where(p => !p.IsPrerelease);
                }

                if (!includeSemVer2)
                {
                    filtered = filtered.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);
                }

                var packages = filtered.OrderBy(p => p.Version).ToList();

                yield return (searchFilters, packages);
            }
        }

        private string EncodePackageId(string key)
        {
            // Keys can only contain letters, digits, underscore(_), dash(-), or equal sign(=).
            // TODO: Align with NuGet.org's algorithm.
            var bytes = Encoding.UTF8.GetBytes(key);
            var base64 = Convert.ToBase64String(bytes);

            return base64.Replace('+', '-').Replace('/', '_');
        }

        private PackageDocument BuildFullDocument(
            string documentKey,
            SearchFilters searchFilters,
            List<Package> packages)
        {
            var latest = packages.Last();
            var dependencies = latest
                .Dependencies
                .Select(d => d.Id?.ToLowerInvariant())
                .Where(d => d != null)
                .Distinct()
                .ToArray();

            var document = new PackageDocument();

            document.Key = documentKey;
            document.Id = latest.Id;
            document.Version = latest.Version.ToFullString();
            document.Description = latest.Description;
            document.Authors = latest.Authors;
            document.IconUrl = latest.IconUrlString;
            document.LicenseUrl = latest.LicenseUrlString;
            document.ProjectUrl = latest.ProjectUrlString;
            document.Published = latest.Published;
            document.Summary = latest.Summary;
            document.Tags = latest.Tags;
            document.Title = latest.Title;
            document.TotalDownloads = packages.Sum(p => p.Downloads);
            document.DownloadsMagnitude = document.TotalDownloads.ToString().Length;
            document.Versions = packages.Select(p => p.Version.ToFullString()).ToArray();
            document.VersionDownloads = packages.Select(p => p.Downloads.ToString()).ToArray();
            document.Dependencies = dependencies;
            document.PackageTypes = latest.PackageTypes.Select(t => t.Name).ToArray();
            document.Frameworks = latest.TargetFrameworks.Select(f => f.Moniker.ToLowerInvariant()).ToArray();
            document.SearchFilters = searchFilters.ToString();

            return document;
        }

        private UpdateDownloads BuildDownloadsDocument(
            string documentKey,
            List<Package> packages)
        {
            var document = new UpdateDownloads();

            document.Key = documentKey;
            document.TotalDownloads = packages.Sum(p => p.Downloads);
            document.DownloadsMagnitude = document.TotalDownloads.ToString().Length;
            document.VersionDownloads = packages.Select(p => p.Downloads.ToString()).ToArray();

            return document;
        }
    }
}
