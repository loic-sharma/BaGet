using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BaGet.Core;
using BaGet.Protocol;
using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;

namespace BaGet.Web
{
    /// <summary>
    /// Controller that implements the legacy NuGet V2 APIs.
    /// </summary>
    [Produces("application/xml")]
    public class V2ApiController : Controller
    {
        private readonly ISearchService _search;
        private readonly IMirrorService _mirror;
        private readonly IV2Builder _builder;

        public V2ApiController(
            ISearchService search,
            IMirrorService mirror,
            IV2Builder builder)
        {
            _search = search ?? throw new ArgumentNullException(nameof(search));
            _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public XElement Index() => _builder.BuildIndex();

        public async Task<XElement> List(
            [FromQuery(Name = "$skip")] int skip = 0,
            [FromQuery(Name = "$top")] int top = 20,
            [FromQuery(Name = "$orderby")] string orderBy = null,
            CancellationToken cancellationToken = default)
        {
            // TODO: Order by
            var search = new SearchRequest
            {
                Skip = skip,
                Take = top,
                IncludePrerelease = true,
                IncludeSemVer2 = true,
            };

            var response = await _search.SearchAsync(search, cancellationToken);

            // TODO: Undo
            var packages = response
                .Data
                .Select(r => new Package
                {
                    Id = r.PackageId,
                    Authors = r.Authors.ToArray(),
                    Description = r.Description,
                    Downloads = r.TotalDownloads,
                    Language = "English",
                    MinClientVersion = "1.2.3",
                    Published = DateTime.Now.AddDays(-1),
                    Summary = r.Summary,

                    IconUrl = new Uri(r.IconUrl),
                    LicenseUrl = new Uri(r.LicenseUrl),
                    ProjectUrl = new Uri(r.ProjectUrl),
                    RepositoryUrl = new Uri(r.ProjectUrl), // TODO

                    Tags = r.Tags.ToArray(),

                    Version = r.ParseVersion(),

                    Dependencies = new List<PackageDependency>()
                })
                .ToList();

            return _builder.BuildPackages(packages);
        }

        public async Task<XElement> Search(
            string searchTerm,
            string targetFramework,
            bool includePrerelease = true,
            CancellationToken cancellationToken = default)
        {
            searchTerm = searchTerm?.Trim('\'') ?? "";
            targetFramework = targetFramework?.Trim('\'') ?? "";

            // TODO: Order by
            var search = new SearchRequest
            {
                Skip = 0,
                Take = 20,
                IncludePrerelease = includePrerelease,
                IncludeSemVer2 = true,
                Query = searchTerm
            };

            var response = await _search.SearchAsync(search, cancellationToken);

            // TODO: Undo
            var packages = response
                .Data
                .Select(r => new Package
                {
                    Id = r.PackageId,
                    Authors = r.Authors.ToArray(),
                    Description = r.Description,
                    Downloads = r.TotalDownloads,
                    Language = "English",
                    MinClientVersion = "1.2.3",
                    Published = DateTime.Now.AddDays(-1),
                    Summary = r.Summary,

                    IconUrl = new Uri(r.IconUrl),
                    LicenseUrl = new Uri(r.LicenseUrl),
                    ProjectUrl = new Uri(r.ProjectUrl),
                    RepositoryUrl = new Uri(r.ProjectUrl), // TODO

                    Tags = r.Tags.ToArray(),

                    Version = r.ParseVersion(),

                    Dependencies = new List<PackageDependency>()
                })
                .ToList();

            return _builder.BuildPackages(packages);
        }

        public async Task<ActionResult<XElement>> Package(string id, CancellationToken cancellationToken)
        {
            id = id?.Trim('\'');

            var packages = await _mirror.FindPackagesAsync(id, cancellationToken);
            if (!packages.Any())
            {
                return NotFound();
            }

            return _builder.BuildPackages(packages);
        }

        public async Task<ActionResult<XElement>> PackageVersion(string id, string version, CancellationToken cancellationToken)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return BadRequest();
            }

            var package = await _mirror.FindPackageOrNullAsync(id, nugetVersion, cancellationToken);
            if (package == null)
            {
                return NotFound();
            }

            return _builder.BuildPackage(package);
        }
    }
}
