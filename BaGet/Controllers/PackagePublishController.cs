using BaGet.Core.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NuGet.Packaging;
using System;
using System.Linq;

namespace BaGet.Controllers
{
    using BagetPackageDependencyGroup = Core.Entities.PackageDependencyGroup;

    public class PackagePublishController : Controller
    {
        public string Upload(IFormFile upload)
        {
            Uri ParseUri(string uriString)
            {
                if (string.IsNullOrEmpty(uriString)) return null;

                return new Uri(uriString);
            }

            string[] ParseTags(string tags)
            {
                if (string.IsNullOrEmpty(tags)) return new string[0];

                return tags.Split(',', ';', '\t', ' ');
            }

            using (var uploadStream = upload.OpenReadStream())
            using (var packageReader = new PackageArchiveReader(uploadStream))
            {
                HttpContext.Response.StatusCode = 201;

                var nuspec = packageReader.NuspecReader;

                var package = new Package
                {
                    Id = nuspec.GetId(),
                    Version = nuspec.GetVersion().ToNormalizedString(),
                    Authors = nuspec.GetAuthors(),
                    Description = nuspec.GetDescription(),
                    Listed = true,
                    MinClientVersion = nuspec.GetMinClientVersion()?.ToNormalizedString(),
                    Published = DateTime.UtcNow,
                    RequireLicenseAcceptance = nuspec.GetRequireLicenseAcceptance(),
                    Summary = nuspec.GetSummary(),
                    Title = nuspec.GetTitle(),
                    IconUrl = ParseUri(nuspec.GetIconUrl()),
                    LicenseUrl = ParseUri(nuspec.GetLicenseUrl()),
                    ProjectUrl = ParseUri(nuspec.GetProjectUrl()),
                    Dependencies = nuspec.GetDependencyGroups()
                                         .Select(g => new BagetPackageDependencyGroup
                                         {
                                             TargetFramework = g.TargetFramework.DotNetFrameworkName,
                                             Dependencies = g.Packages
                                                             .Select(p => new PackageDependency
                                                             {
                                                                 Id = p.Id,
                                                                 VersionRange = p.VersionRange.OriginalString
                                                             })
                                                             .ToList()
                                         })
                                         .ToList(),
                    Tags = ParseTags(nuspec.GetTags())
                };

                return $"{package.Id}@{package.Version}";
            }
        }

        public void Delete(string id, string version)
        {
            HttpContext.Response.StatusCode = 404;
        }

        public void Relist(string id, string version)
        {
            HttpContext.Response.StatusCode = 404;
        }
    }
}
