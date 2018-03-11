using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Entities;
using BaGet.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGet.Packaging;

namespace BaGet.Controllers
{
    using BagetPackageDependencyGroup = Core.Entities.PackageDependencyGroup;

    public class PackagePublishController : Controller
    {
        private readonly BaGetContext _context;

        public PackagePublishController(BaGetContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        // See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package
        public async Task Upload(IFormFile upload)
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

            try
            {
                using (var uploadStream = upload.OpenReadStream())
                using (var packageReader = new PackageArchiveReader(uploadStream))
                {
                    var nuspec = packageReader.NuspecReader;

                    _context.Packages.Add(new Package
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
                        Dependencies = nuspec
                                        .GetDependencyGroups()
                                        .Select(group => new BagetPackageDependencyGroup
                                        {
                                            TargetFramework = group.TargetFramework.DotNetFrameworkName,
                                            Dependencies = group.Packages
                                                            .Select(p => new PackageDependency
                                                            {
                                                                Id = p.Id,
                                                                VersionRange = p.VersionRange.OriginalString
                                                            })
                                                            .ToList()
                                        })
                                        .ToList(),
                        Tags = ParseTags(nuspec.GetTags())
                    });
                }
            }
            catch
            {
                // TODO: Log?
                HttpContext.Response.StatusCode = 400;
                return;
            }

            try
            {
                await _context.SaveChangesAsync();

                HttpContext.Response.StatusCode = 201;
            }
            catch (DbUpdateException e) when (e.IsUniqueConstraintViolationException())
            {
                HttpContext.Response.StatusCode = 409;
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
