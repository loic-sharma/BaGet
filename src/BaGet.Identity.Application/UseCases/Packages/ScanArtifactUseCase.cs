using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Application.Models.Policies;
using BaGet.Identity.Application.Security;

namespace BaGet.Identity.Application.UseCases.Packages;

/// <summary>
/// <em>Scan now</em> for one version: checks the artifact (and its dependencies, or the image's packages) against the vulnerability source
/// again and shows what was found. Linked from the findings section of every format's package page. Gated like the other package operations:
/// signed in, the Manage action on the package (<see cref="ScanAccess"/>); refused with 503 while the server is read-only (the result is
/// stored). The scan of a container image can take minutes, so it is queued for the background worker and the screen says so; the other
/// formats are scanned before the answer. Works in the request's repository.
/// </summary>
public sealed class ScanArtifactUseCase
{
    private readonly PackageScreenServices _services;
    private readonly IScanService _scans;
    private readonly IAdminAudit _audit;
    private readonly ScanQueue _queue;

    /// <summary>Creates the use case.</summary>
    public ScanArtifactUseCase(PackageScreenServices services, IScanService scans, IAdminAudit audit, ScanQueue queue)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _scans = scans ?? throw new ArgumentNullException(nameof(scans));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <summary>Shows the form (and the stored result) of the version.</summary>
    /// <param name="caller">The signed-in user.</param>
    /// <param name="name">The artifact name (NuGet id, npm name, Maven <c>group:artifact</c>, Docker image).</param>
    /// <param name="version">The version (Docker: the manifest digest).</param>
    /// <param name="returnUrl">Where the link back goes, as the visitor sent it.</param>
    /// <param name="backUrl">The address the link back leads to: <paramref name="returnUrl"/> when it is local, else the home page (the host knows its path base).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<ScreenResult<ScanViewModel>> GetAsync(ClaimsPrincipal caller, string? name, string? version, string? returnUrl, string backUrl, CancellationToken cancellationToken)
    {
        if (!await CanScanAsync(caller, name, version, cancellationToken).ConfigureAwait(false)) return ScreenResult<ScanViewModel>.Forbidden();

        var model = Model(name, version, returnUrl, backUrl);
        model.Engine = _scans.GetEngineStatus();
        model.Result = await _scans.FindAsync(name!, version!, cancellationToken).ConfigureAwait(false);

        return ScreenResult<ScanViewModel>.Ok(model);
    }

    /// <summary>Scans the version now (or queues it, for an image) and shows the outcome.</summary>
    /// <inheritdoc cref="GetAsync"/>
    public async Task<ScreenResult<ScanViewModel>> ScanAsync(ClaimsPrincipal caller, string? name, string? version, string? returnUrl, string backUrl, CancellationToken cancellationToken)
    {
        if (!await CanScanAsync(caller, name, version, cancellationToken).ConfigureAwait(false)) return ScreenResult<ScanViewModel>.Forbidden();

        var model = Model(name, version, returnUrl, backUrl);
        model.Engine = _scans.GetEngineStatus();
        if (!model.Engine.Available)
        {
            return Refuse(model, 409, model.Engine.Reason ?? "Scanning is not available.");
        }

        var repository = _services.Repository.Current;
        var user = (await _services.Users.GetUserAsync(caller).ConfigureAwait(false))?.UserName;
        _audit.Record(user, "scan.run", $"repository {repository.Name}, {name} {version}");

        try
        {
            var scanner = _services.Services.GetFormatService<IFormatScanner>(repository.Format);
            if (scanner?.Engine == ScanEngine.Image)
            {
                if (!await _scans.IsStoredAsync(name!, version!, cancellationToken).ConfigureAwait(false)) return Refuse(model, 404, "That version does not exist in this repository.");

                _queue.Enqueue(repository, name!, version!);
                model.Result = await _scans.FindAsync(name!, version!, cancellationToken).ConfigureAwait(false);
                model.Message = "The image is queued for a scan with Trivy, which can take a few minutes. Reload this page to see the result.";

                return ScreenResult<ScanViewModel>.Ok(model);
            }

            model.Result = await _scans.ScanAsync(name!, version!, cancellationToken).ConfigureAwait(false);
        }
        catch (ScanUnavailableException e)
        {
            return Refuse(model, 409, e.Message);
        }

        if (model.Result == null) return Refuse(model, 404, "That version does not exist in this repository.");

        model.Result = await _scans.FindAsync(name!, version!, cancellationToken).ConfigureAwait(false);
        model.Message = model.Result?.Error != null && model.Result.ScannedAt == null
            ? "The scan failed: " + model.Result.Error
            : model.Result?.FindingCount > 0
                ? $"Scanned: {model.Result.FindingCount} finding(s), the worst is {ScanSeverities.Name(model.Result.Highest)}."
                : "Scanned: no known vulnerabilities.";
        model.Failed = model.Result?.Error != null && model.Result.ScannedAt == null;

        return ScreenResult<ScanViewModel>.Ok(model);
    }

    private async Task<bool> CanScanAsync(ClaimsPrincipal caller, string? name, string? version, CancellationToken cancellationToken)
        => !string.IsNullOrEmpty(name)
           && !string.IsNullOrEmpty(version)
           && await ScanAccess.CanScanAsync(_services.Services, caller, name, cancellationToken).ConfigureAwait(false);

    // The screen is shown again with the reason, under the status the refusal stands for.
    private static ScreenResult<ScanViewModel> Refuse(ScanViewModel model, int status, string message)
    {
        model.Message = message;
        model.Failed = true;

        return ScreenResult<ScanViewModel>.Ok(model, status);
    }

    private static ScanViewModel Model(string? name, string? version, string? returnUrl, string backUrl)
        => new() { Name = name, Version = version, ReturnUrl = returnUrl, BackUrl = backUrl };
}
