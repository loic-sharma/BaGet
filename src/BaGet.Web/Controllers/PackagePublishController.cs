using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using NuGet.Versioning;

namespace BaGet.Web
{
    public class PackagePublishController : Controller
    {
        private readonly IAuthenticationService _authentication;
        private readonly IPackageIndexingService _indexer;
        private readonly IPackageDatabase _packages;
        private readonly IPackageDeletionService _deleteService;
        private readonly IPackageStorageService _storageService;
        private readonly IOptionsSnapshot<BaGetOptions> _options;
        private readonly ILogger<PackagePublishController> _logger;

        public PackagePublishController(
            IAuthenticationService authentication,
            IPackageIndexingService indexer,
            IPackageDatabase packages,
            IPackageDeletionService deletionService,
            IPackageStorageService storageService,
            IOptionsSnapshot<BaGetOptions> options,
            ILogger<PackagePublishController> logger)
        {
            _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _deleteService = deletionService ?? throw new ArgumentNullException(nameof(deletionService));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package
        public async Task Upload(CancellationToken cancellationToken)
        {
            if (_options.Value.IsReadOnlyMode ||
                !await _authentication.AuthenticateAsync(Request.GetApiKey(), cancellationToken))
            {
                HttpContext.Response.StatusCode = 401;
                return;
            }

            try
            {
                using (var uploadStream = await Request.GetUploadStreamOrNullAsync(cancellationToken))
                {
                    if (uploadStream == null)
                    {
                        HttpContext.Response.StatusCode = 400;
                        return;
                    }

                    var result = await _indexer.IndexAsync(uploadStream, cancellationToken);

                    switch (result)
                    {
                        case PackageIndexingResult.InvalidPackage:
                            HttpContext.Response.StatusCode = 400;
                            break;

                        case PackageIndexingResult.PackageAlreadyExists:
                            HttpContext.Response.StatusCode = 409;
                            break;

                        case PackageIndexingResult.Success:
                            HttpContext.Response.StatusCode = 201;
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception thrown during package upload");

                HttpContext.Response.StatusCode = 500;
            }
        }

        [HttpDelete]
        public async Task<IActionResult> Delete(string id, string version, CancellationToken cancellationToken)
        {
            if (_options.Value.IsReadOnlyMode)
            {
                return Unauthorized();
            }

            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            if (!await _authentication.AuthenticateAsync(Request.GetApiKey(), cancellationToken))
            {
                return Unauthorized();
            }

            if (await _deleteService.TryDeletePackageAsync(id, nugetVersion, cancellationToken))
            {
                return NoContent();
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost]
        public async Task<IActionResult> Relist(string id, string version, CancellationToken cancellationToken)
        {
            if (_options.Value.IsReadOnlyMode)
            {
                return Unauthorized();
            }

            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            if (!await _authentication.AuthenticateAsync(Request.GetApiKey(), cancellationToken))
            {
                return Unauthorized();
            }

            if (await _packages.RelistPackageAsync(id, nugetVersion, cancellationToken))
            {
                return Ok();
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost]
        public async Task<IActionResult> Repackage(string id, string version, string newVersion, CancellationToken cancellationToken)
        {
            if (_options.Value.IsReadOnlyMode)
            {
                return Unauthorized();
            }

            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            if (!NuGetVersion.TryParse(newVersion, out var newNugetVersion))
            {
                return BadRequest("Invalid version");
            }

            if (!await _authentication.AuthenticateAsync(Request.GetApiKey(), cancellationToken))
            {
                return Unauthorized();
            }

            var packageStream = await _storageService.GetPackageStreamAsync(id, nugetVersion, cancellationToken);

            if (packageStream == null)
            {
                return NotFound();
            }

            if(await _packages.ExistsAsync(id, newNugetVersion, cancellationToken))
            {
                return BadRequest("Package version already exists");
            }

            using (var ms = new MemoryStream())
            {
                await packageStream.CopyToAsync(ms);
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Update))
                {
                    var nuspecEntry = archive.Entries.FirstOrDefault(x => x.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

                    if (nuspecEntry == null)
                    {
                        return BadRequest("Nuget file is missing nuspec");
                    }

                    string updatedNuspec;
                    using (var nuspecStream = nuspecEntry.Open())
                    {
                        var doc = XDocument.Load(nuspecStream);
                        var ns = XNamespace.Get("http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd");
                        doc.Descendants(ns + "version").First().Value = newNugetVersion.ToNormalizedString();
                        using(var docMs = new MemoryStream())
                        {
                            doc.Save(docMs);
                            updatedNuspec = Encoding.UTF8.GetString(docMs.ToArray());
                        }
                    }

                    nuspecEntry.Delete();
                    nuspecEntry = archive.CreateEntry($"{id}.nuspec");

                    using(var writer = new StreamWriter(nuspecEntry.Open()))
                    {
                        writer.Write(updatedNuspec);
                    }
                }


                using(var repackgedStream = new MemoryStream(ms.ToArray()))
                {
                    await _indexer.IndexAsync(repackgedStream, cancellationToken);
                }

                return Ok();
            }
        }
    }
}
