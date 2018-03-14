using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Entities;
using BaGet.Core.Extensions;
using BaGet.Core.Indexing;
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
        private readonly IIndexingService _indexer;
        private readonly ILogger<PackagePublishController> _logger;

        public PackagePublishController(
            IIndexingService indexer,
            ILogger<PackagePublishController> logger)
        {
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package
        public async Task Upload(IFormFile upload)
        {
            if (upload == null)
            {
                HttpContext.Response.StatusCode = 400;
                return;
            }

            try
            {
                using (var uploadStream = upload.OpenReadStream())
                {
                    var result = await _indexer.IndexAsync(uploadStream);

                    switch (result)
                    {
                        case Result.InvalidPackage:
                            HttpContext.Response.StatusCode = 400;
                            break;

                        case Result.PackageAlreadyExists:
                            HttpContext.Response.StatusCode = 409;
                            break;

                        case Result.Success:
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
