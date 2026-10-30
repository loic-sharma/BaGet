using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Mvc.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using BaGet.Web.V3.Extensions;
using BaGet.Web.V3.Filters;

namespace BaGet.Web.V3.Controllers.Protocols;

public class PackagePublishController : ControllerBase
{
    private readonly IAuthenticationService _authentication;
    private readonly IPackageAuthorizationService _authorization;
    private readonly IPackageSubmissionService _submissions;
    private readonly IPackageDatabase _packages;
    private readonly IPackageDeletionService _deleteService;
    private readonly IOptionsSnapshot<BaGetOptions> _options;
    private readonly ILogger<PackagePublishController> _logger;
    private readonly PackageChangeNotifier _notifier;
    private readonly IScanGate _scanGate;

    public PackagePublishController(
        IAuthenticationService authentication,
        IPackageSubmissionService submissions,
        IPackageDatabase packages,
        IPackageDeletionService deletionService,
        IOptionsSnapshot<BaGetOptions> options,
        ILogger<PackagePublishController> logger,
        PackageChangeNotifier notifier,
        IPackageAuthorizationService authorization,
        IScanGate scanGate)
    {
        _scanGate = scanGate ?? throw new ArgumentNullException(nameof(scanGate));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _deleteService = deletionService ?? throw new ArgumentNullException(nameof(deletionService));
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

                // Whether the caller may push depends on the package ID, which is inside the
                // package. An unreadable package is left for the indexer to reject.
                var packageId = uploadStream.ReadPackageIdOrNull();
                if (packageId != null &&
                    !await _authorization.AuthorizeAsync(principal, PackageAction.Push, packageId, cancellationToken))
                {
                    HttpContext.Response.StatusCode = 403;
                    return;
                }

                // The scanning policy of the repository may refuse a package with known vulnerabilities (403 with the reason).
                if (_scanGate.Enabled && await _scanGate.BlocksDeploysAsync(cancellationToken))
                {
                    var components = NuGetScanComponents.TryRead(uploadStream);
                    if (components != null) await _scanGate.CheckDeployAsync(components, cancellationToken);
                }

                // Decided before the push: afterwards every ID exists.
                var isNewId = packageId != null && !await _packages.ExistsAsync(packageId, cancellationToken);

                var result = await _submissions.SubmitAsync(uploadStream, cancellationToken);

                // An accepted package claims its ID now, as it would on nuget.org: it is
                // the pusher's even while it is validated.
                if (result == PackageIndexingResult.Success || result == PackageIndexingResult.Accepted)
                {
                    await _authorization.OnPushedAsync(principal, packageId, isNewId, cancellationToken);
                }

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

                    case PackageIndexingResult.Accepted:
                        HttpContext.Response.StatusCode = 202;
                        break;
                }
            }
        }
        catch (Exception e) when (!(e is MaintenanceException) && !(e is ScanBlockedException))
        {
            // A refused write (read-only maintenance) is not a server fault: MaintenanceMiddleware answers 503, and a scanning
            // policy refusal is answered 403 with its reason by ScanBlockedFilter.
            _logger.LogError(e, "Exception thrown during package upload");

            HttpContext.Response.StatusCode = 500;
        }
    }

    private async Task<IActionResult?> AuthorizeManageAsync(string? id, CancellationToken cancellationToken)
    {
        var principal = await _authentication.AuthenticatePrincipalAsync(Request.GetApiKey(), cancellationToken);
        if (principal == null)
        {
            return Unauthorized();
        }

        if (!await _authorization.AuthorizeAsync(principal, PackageAction.Manage, id, cancellationToken))
        {
            return StatusCode(403);
        }

        return null;
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(string? id, string version, CancellationToken cancellationToken)
    {
        if (_options.Value.IsReadOnlyMode)
        {
            return Unauthorized();
        }

        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        var denied = await AuthorizeManageAsync(id, cancellationToken);
        if (denied != null)
        {
            return denied;
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
    public async Task<IActionResult> Relist(string? id, string version, CancellationToken cancellationToken)
    {
        if (_options.Value.IsReadOnlyMode)
        {
            return Unauthorized();
        }

        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        var denied = await AuthorizeManageAsync(id, cancellationToken);
        if (denied != null)
        {
            return denied;
        }

        if (await _packages.RelistPackageAsync(id, nugetVersion, cancellationToken))
        {
            await _notifier.PackageChangedAsync(id, nugetVersion, cancellationToken);

            return Ok();
        }
        else
        {
            return NotFound();
        }
    }
}
