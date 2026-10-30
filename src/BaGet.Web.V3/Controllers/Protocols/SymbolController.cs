using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Http;
using BaGet.Frontend.Mvc.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BaGet.Web.V3.Extensions;
using BaGet.Web.V3.Filters;

namespace BaGet.Web.V3.Controllers.Protocols;

public class SymbolController : ControllerBase
{
    private readonly IAuthenticationService _authentication;
    private readonly IPackageAuthorizationService _authorization;
    private readonly ISymbolIndexingService _indexer;
    private readonly ISymbolStorageService _storage;
    private readonly IOptionsSnapshot<BaGetOptions> _options;
    private readonly ILogger<SymbolController> _logger;

    public SymbolController(
        IAuthenticationService authentication,
        ISymbolIndexingService indexer,
        ISymbolStorageService storage,
        IOptionsSnapshot<BaGetOptions> options,
        ILogger<SymbolController> logger,
        IPackageAuthorizationService authorization)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package
    [RemoteRepositoryReadOnly]
    [UploadSizeLimit]
    public async Task Upload(CancellationToken cancellationToken)
    {
        var principal = _options.Value.IsReadOnlyMode
            ? null
            : await _authentication.AuthenticatePrincipalAsync(Request.GetApiKey(), cancellationToken);
        if (principal == null)
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

                var packageId = uploadStream.ReadPackageIdOrNull();
                if (packageId != null &&
                    !await _authorization.AuthorizeAsync(principal, PackageAction.Push, packageId, cancellationToken))
                {
                    HttpContext.Response.StatusCode = 403;
                    return;
                }

                var result = await _indexer.IndexAsync(uploadStream, cancellationToken);

                switch (result)
                {
                    case SymbolIndexingResult.InvalidSymbolPackage:
                        HttpContext.Response.StatusCode = 400;
                        break;

                    case SymbolIndexingResult.PackageNotFound:
                        // With validation on the package is published only when it is released: say so.
                        HttpContext.Response.StatusCode = 404;
                        HttpContext.Response.ContentType = "text/plain";
                        await HttpContext.Response.WriteAsync(
                            "The package this symbol package belongs to is not published. With validation enabled it is "
                            + "published only after validation passes: push the symbols again then.",
                            cancellationToken);
                        break;

                    case SymbolIndexingResult.Success:
                        HttpContext.Response.StatusCode = 201;
                        break;
                }
            }
        }
        catch (Exception e) when (!(e is MaintenanceException))
        {
            // A refused write (read-only maintenance) is not a server fault: MaintenanceMiddleware answers 503.
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
