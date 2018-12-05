﻿using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Services;
using BaGet.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BaGet.Controllers
{
    public class SymbolController : Controller
    {
        private readonly IAuthenticationService _authentication;
        private readonly ISymbolIndexingService _indexer;
        private readonly ISymbolStorageService _storage;
        private readonly ILogger<SymbolController> _logger;

        public SymbolController(
            IAuthenticationService authentication,
            ISymbolIndexingService indexer,
            ISymbolStorageService storage,
            ILogger<SymbolController> logger)
        {
            _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package
        public async Task Upload(CancellationToken cancellationToken)
        {
            if (!await _authentication.AuthenticateAsync(Request.GetApiKey()))
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
                        case SymbolIndexingResult.InvalidSymbolPackage:
                            HttpContext.Response.StatusCode = 400;
                            break;

                        case SymbolIndexingResult.PackageNotFound:
                            HttpContext.Response.StatusCode = 404;
                            break;

                        case SymbolIndexingResult.Success:
                            HttpContext.Response.StatusCode = 201;
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception thrown during symbol upload");

                HttpContext.Response.StatusCode = 500;
            }
        }

        public async Task<IActionResult> Get(string file, string key)
        {
            var pdbStream = await _storage.GetPortablePdbContentStreamOrNullAsync(file, key);
            if (pdbStream == null)
            {
                return NotFound();
            }

            return File(pdbStream, "application/octet-stream");
        }
    }
}
