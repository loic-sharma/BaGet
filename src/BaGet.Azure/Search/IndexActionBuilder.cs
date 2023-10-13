namespace BaGet.Azure;

public class IndexActionBuilder
{
    public virtual IReadOnlyList<IndexAction<KeyedDocument>> AddPackage(PackageRegistration registration)
    {
        return AddOrUpdatePackage(registration, isUpdate: false);
    }

    public virtual IReadOnlyList<IndexAction<KeyedDocument>> UpdatePackage(PackageRegistration registration)
    {
        return AddOrUpdatePackage(registration, isUpdate: true);
    }

    private IReadOnlyList<IndexAction<KeyedDocument>> AddOrUpdatePackage(PackageRegistration registration, bool isUpdate)
    {
        var encodedId = EncodePackageId(registration.PackageId.ToLowerInvariant());
        var result = new List<IndexAction<KeyedDocument>>();

        for (var i = 0; i < 4; i++)
        {
            var includePrerelease = (i & 1) != 0;
            var includeSemVer2 = (i & 2) != 0;
            var searchFilters = (SearchFilters)i;

            var documentKey = $"{encodedId}-{searchFilters}";
            var filtered = registration.Packages.Where(p => p.Listed);

            if (!includePrerelease)
            {
                filtered = filtered.Where(p => !p.IsPrerelease);
            }

            if (!includeSemVer2)
            {
                filtered = filtered.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);
            }

            var versions = filtered.OrderBy(p => p.Version).ToList();
            if (versions.Count == 0)
            {
                if (isUpdate)
                {
                    var action = IndexAction.Delete(
                        new KeyedDocument
                        {
                            Key = documentKey
                        });

                    result.Add(action);
                }

                continue;
            }

            var latest = versions.Last();
            var dependencies = latest
                .Dependencies
                .Select(d => d.Id?.ToLowerInvariant())
                .Where(d => d != null)
                .Distinct()
                .ToArray();

            var document = new PackageDocument
            {
                Key = $"{encodedId}-{searchFilters}",
                Id = latest.Id,
                Version = latest.Version.ToFullString(),
                Description = latest.Description,
                Authors = latest.Authors,
                HasEmbeddedIcon = latest.HasEmbeddedIcon,
                IconUrl = latest.IconUrlString,
                LicenseUrl = latest.LicenseUrlString,
                ProjectUrl = latest.ProjectUrlString,
                Published = latest.Published,
                Summary = latest.Summary,
                Tags = latest.Tags,
                Title = latest.Title,
                TotalDownloads = versions.Sum(p => p.Downloads),
                Versions = versions.Select(p => p.Version.ToFullString()).ToArray(),
                VersionDownloads = versions.Select(p => p.Downloads.ToString()).ToArray(),
                Dependencies = dependencies,
                PackageTypes = latest.PackageTypes.Select(t => t.Name).ToArray(),
                Frameworks = latest.TargetFrameworks.Select(f => f.Moniker.ToLowerInvariant()).ToArray(),
                SearchFilters = searchFilters.ToString(),
                DownloadsMagnitude = versions.Sum(p => p.Downloads).ToString().Length
            };

            result.Add(isUpdate ? IndexAction.MergeOrUpload<KeyedDocument>(document) : IndexAction.Upload<KeyedDocument>(document));
        }

        return result;
    }

    private string EncodePackageId(string key)
    {
        // Keys can only contain letters, digits, underscore(_), dash(-), or equal sign(=).
        // TODO: Align with NuGet.org's algorithm.
        var bytes = Encoding.UTF8.GetBytes(key);
        var base64 = Convert.ToBase64String(bytes);

        return base64.Replace('+', '-').Replace('/', '_');
    }
}
