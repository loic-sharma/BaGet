using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Entities;
using BaGet.Core.Extensions;
using BaGet.Core.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace BaGet.Controllers
{
    using BagetPackageDependencyGroup = Core.Entities.PackageDependencyGroup;
    using BaGetPackageDependency = Core.Entities.PackageDependency;

    public class PackagePublishController : Controller
    {
        private readonly BaGetContext _context;
        private readonly IPackageStorageService _storage;
        private readonly ILogger<PackagePublishController> _logger;

        public PackagePublishController(
            BaGetContext context,
            IPackageStorageService storage,
            ILogger<PackagePublishController> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            Package package;

            try
            {
                using (var uploadStream = upload.OpenReadStream())
                using (var packageReader = new PackageArchiveReader(uploadStream))
                {
                    if (await PackageAlreadyExistsAsync(packageReader.GetIdentity()))
                    {
                        HttpContext.Response.StatusCode = 409;
                        return;
                    }

                    try
                    {
                        await _storage.SaveAsync(packageReader, uploadStream);
                    }
                    catch (Exception e)
                    {
                        // This may happen due to concurrent pushes.
                        _logger.LogError(e, "Failed to save package {Identity}", packageReader.GetIdentity());

                        HttpContext.Response.StatusCode = 500;
                        return;
                    }

                    var nuspec = packageReader.NuspecReader;

                    package = new Package
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
                                    .Select(p => new BaGetPackageDependency
                                    {
                                        Id = p.Id,
                                        VersionRange = p.VersionRange.OriginalString
                                    })
                                    .ToList()
                            })
                            .ToList(),
                        Tags = ParseTags(nuspec.GetTags())
                    };
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Uploaded package is invalid");

                HttpContext.Response.StatusCode = 400;
                return;
            }

            try
            {
                _context.Packages.Add(package);

                await _context.SaveChangesAsync();

                HttpContext.Response.StatusCode = 201;
            }
            catch (DbUpdateException e) when (e.IsUniqueConstraintViolationException())
            {
                _logger.LogError(e,
                    "Failed to upload package {PackageId} {PackageVersion} as it already exists",
                    package.Id,
                    package.Version);

                HttpContext.Response.StatusCode = 409;
            }
        }

        private Task<bool> PackageAlreadyExistsAsync(PackageIdentity package)
        {
            return _context.Packages
                .Where(p => p.Id == package.Id)
                .Where(p => p.Version == package.Version.ToNormalizedString())
                .AnyAsync();
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
