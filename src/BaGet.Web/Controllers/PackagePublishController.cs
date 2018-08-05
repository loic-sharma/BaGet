using System;
using System.Threading.Tasks;
using BaGet.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Controllers
{
    [Authorize]
    public class PackagePublishController : Controller
    {
        public const string ApiKeyHeader = "X-NuGet-ApiKey";

        private readonly IAuthenticationService _authentication;
        private readonly IIndexingService _indexer;
        private readonly IPackageService _packages;
        private readonly ILogger<PackagePublishController> _logger;

        public PackagePublishController(
            IAuthenticationService authentication,
            IIndexingService indexer,
            IPackageService packages,
            ILogger<PackagePublishController> logger)
        {
            _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package
        public async Task Upload(IFormFile package)
        {
            if (package == null)
            {
                HttpContext.Response.StatusCode = 400;
                return;
            }

            if (!await _authentication.AuthenticateAsync(ApiKey))
            {
                HttpContext.Response.StatusCode = 401;
                return;
            }

            try
            {
                using (var uploadStream = package.OpenReadStream())
                {
                    var result = await _indexer.IndexAsync(uploadStream);

                    switch (result)
                    {
                        case IndexingResult.InvalidPackage:
                            HttpContext.Response.StatusCode = 400;
                            break;

                        case IndexingResult.PackageAlreadyExists:
                            HttpContext.Response.StatusCode = 409;
                            break;

                        case IndexingResult.Success:
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

        public async Task<IActionResult> Delete(string id, string version)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            if (!await _authentication.AuthenticateAsync(ApiKey))
            {
                return Unauthorized();
            }

            if (await _packages.UnlistPackageAsync(id, nugetVersion))
            {
                return NoContent();
            }
            else
            {
                return NotFound();
            }
        }

        public async Task<IActionResult> Relist(string id, string version)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            if (!await _authentication.AuthenticateAsync(ApiKey))
            {
                return Unauthorized();
            }

            if (await _packages.RelistPackageAsync(id, nugetVersion))
            {
                return Ok();
            }
            else
            {
                return NotFound();
            }
        }

        private string ApiKey => Request.Headers[ApiKeyHeader];
    }
}
