using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.UseCases.Packages;

/// <summary>
/// The owners' screen for a package ID (<c>/packages/{id}/manage</c>): its owners, its versions' listing and deprecation, vulnerabilities and
/// indexed symbols (PDBs), and the abuse reports against it. An administrator may open it for any ID, ownerless ones included, and alone may
/// set the ID's verified (reserved) flag and delete permanently whatever <see cref="BaGetOptions.PackageDeletionBehavior"/> says, after
/// confirming their password. Everything works in the request's repository. Scoped: <see cref="EnterAsync"/> runs once per request (the view
/// controller asks first, so the check keeps its place before the repository filter; the operations ask again and get the same answer).
/// </summary>
public sealed class PackageManageUseCase
{
    /// <summary>
    /// What every write handler answers on a read-only server. The API refuses each of these operations with a 401, so the screen must
    /// refuse all of them too rather than only the one it was last asked about.
    /// </summary>
    public const string ReadOnlyMessage = PackageScreenServices.ReadOnlyMessage;

    private readonly PackageScreenServices _services;
    private readonly IPackageDatabase _packages;
    private readonly PackageChangeNotifier _notifier;
    private readonly IContext _context;
    private readonly IPackageDeletionService _deletions;
    private readonly ISymbolRegistry _symbols;
    private readonly SystemTime _time;
    private readonly ILogger<PackageManageUseCase> _logger;

    private bool _entered;
    private string? _enteredId;
    private bool _enteredWrite;
    private ScreenResult<PackageManageScreen>? _refusal;
    private bool _isAdministrator;
    private bool _actingAsAdministrator;

    /// <summary>Creates the use case.</summary>
    public PackageManageUseCase(
        PackageScreenServices services,
        IPackageDatabase packages,
        PackageChangeNotifier notifier,
        IContext context,
        IPackageDeletionService deletions,
        ISymbolRegistry symbols,
        SystemTime time,
        ILogger<PackageManageUseCase> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _deletions = deletions ?? throw new ArgumentNullException(nameof(deletions));
        _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The check that runs before every handler: only an owner, directly or through an organization, or an administrator, may manage the
    /// package; a write is refused on a read-only server. Returns the answer that ends the request, or null to run the handler.
    /// </summary>
    /// <param name="caller">The signed-in user.</param>
    /// <param name="id">The package ID from the address.</param>
    /// <param name="handler">The handler about to run (for the administrator audit line).</param>
    /// <param name="isWrite">Whether the request is a POST.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<ScreenResult<PackageManageScreen>?> EnterAsync(ClaimsPrincipal caller, string? id, string? handler, bool isWrite, CancellationToken cancellationToken)
    {
        if (_entered && _enteredId == id && _enteredWrite == isWrite) return _refusal;

        _refusal = await CheckAsync(caller, id, handler, isWrite, cancellationToken).ConfigureAwait(false);
        _entered = true;
        _enteredId = id;
        _enteredWrite = isWrite;

        return _refusal;
    }

    private async Task<ScreenResult<PackageManageScreen>?> CheckAsync(ClaimsPrincipal caller, string? id, string? handler, bool isWrite, CancellationToken cancellationToken)
    {
        var isOwner = await _services.Ownership.IsOwnerAsync(_services.Users.GetUserId(caller), caller.Identity?.Name, id, cancellationToken).ConfigureAwait(false);
        _isAdministrator = await _services.Administrators.IsAdministratorAsync(caller, cancellationToken).ConfigureAwait(false);
        // An owner also needs a management permission in this repository (a seeded target gives every signed-in user
        // all of them, so nothing changes until an administrator narrows it); an administrator holds every action. Each
        // handler then asks for the action the protocol asks for the same change (RequireAsync).
        if (isOwner && !_isAdministrator
            && !await ManagePermission.HeldAnyAsync(_services.Services, caller, id, cancellationToken).ConfigureAwait(false))
        {
            isOwner = false;
        }

        if (!isOwner && !_isAdministrator)
        {
            // A caller who may not even read the package gets the answer of a missing one (404), not a 403 that tells the repository exists.
            return _services.Repository.Current.Kind == RepositoryKind.Virtual
                || !await _services.CanSeeAsync(caller, id, cancellationToken).ConfigureAwait(false)
                ? ScreenResult<PackageManageScreen>.NotFound()
                : ScreenResult<PackageManageScreen>.Forbidden();
        }

        _actingAsAdministrator = _isAdministrator && !isOwner;
        // Every handler on this screen but the GET writes to the database, and most of
        // them notify the catalog as well. Refused here, once, rather than in each
        // handler: unlisting, relisting, deprecating and changing owners are the same
        // operations the push and metadata endpoints short-circuit in read-only mode,
        // and a screen that refused only one of them would contradict itself.
        if (_services.IsReadOnlyMode && isWrite)
        {
            return ScreenResult<PackageManageScreen>.Ok(await LoadAsync(caller, id, ReadOnlyMessage, cancellationToken).ConfigureAwait(false));
        }

        if (_actingAsAdministrator && isWrite)
        {
            // The audit trail: an administrator changing what is not theirs.
            _logger.LogWarning("Administrator {Admin} ran {Handler} on {PackageId}", caller.Identity?.Name, handler, id);
        }

        return null;
    }

    /// <summary>
    /// Null when the user may do what the handler does: an administrator, or an owner holding the access model action the protocol
    /// endpoints require for the same change (Delete to unlist, relist and delete, Annotate to deprecate and edit vulnerabilities,
    /// Manage for owners); otherwise the answer that ends the request.
    /// </summary>
    private async Task<ScreenResult<PackageManageScreen>?> RequireAsync(ClaimsPrincipal caller, string? id, AccessAction action, CancellationToken cancellationToken)
    {
        if (_isAdministrator || await ManagePermission.HeldAsync(_services.Services, caller, id, action, cancellationToken).ConfigureAwait(false)) return null;

        return ScreenResult<PackageManageScreen>.Forbidden();
    }

    /// <summary>The GET handler: loads the screen, 404 when the ID has neither versions nor owners.</summary>
    public async Task<ScreenResult<PackageManageScreen>> GetAsync(ClaimsPrincipal caller, string? id, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, null, false, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var screen = await LoadAsync(caller, id, null, cancellationToken).ConfigureAwait(false);

        return screen.Versions.Count == 0 && screen.Owners.Count == 0
            ? ScreenResult<PackageManageScreen>.NotFound()
            : ScreenResult<PackageManageScreen>.Ok(screen);
    }

    /// <summary>Adds an owner.</summary>
    public async Task<ScreenResult<PackageManageScreen>> AddOwnerAsync(ClaimsPrincipal caller, string? id, string? ownerName, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "AddOwner", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        refusal = await RequireAsync(caller, id, AccessAction.Manage, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var result = string.IsNullOrWhiteSpace(ownerName)
            ? OwnerChangeResult.UnknownAccount
            : await _services.Ownership.AddOwnerAsync(id, ownerName.Trim(), cancellationToken).ConfigureAwait(false);

        return await DoneAsync(caller, id, result == OwnerChangeResult.Success ? $"{ownerName} is now an owner." : Describe(result), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes an owner.</summary>
    public async Task<ScreenResult<PackageManageScreen>> RemoveOwnerAsync(ClaimsPrincipal caller, string? id, string? ownerName, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "RemoveOwner", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        refusal = await RequireAsync(caller, id, AccessAction.Manage, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        #pragma warning disable CS8604
        var result = await _services.Ownership.RemoveOwnerAsync(id, ownerName, cancellationToken).ConfigureAwait(false);
        #pragma warning restore CS8604

        return await DoneAsync(caller, id, result == OwnerChangeResult.Success ? $"{ownerName} is no longer an owner." : Describe(result), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Unlists a version.</summary>
    public async Task<ScreenResult<PackageManageScreen>> UnlistAsync(ClaimsPrincipal caller, string? id, string? version, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "Unlist", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        refusal = await RequireAsync(caller, id, AccessAction.Delete, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        return await ChangeVersionAsync(caller, id, version, v => _packages.UnlistPackageAsync(id!, v, cancellationToken), "unlisted", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Relists a version.</summary>
    public async Task<ScreenResult<PackageManageScreen>> RelistAsync(ClaimsPrincipal caller, string? id, string? version, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "Relist", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        refusal = await RequireAsync(caller, id, AccessAction.Delete, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        return await ChangeVersionAsync(caller, id, version, v => _packages.RelistPackageAsync(id!, v, cancellationToken), "relisted", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletion as the server is configured to perform it: <see cref="PackageDeletionBehavior.Unlist"/> hides the version,
    /// <see cref="PackageDeletionBehavior.HardDelete"/> removes its rows and its files. The screen labels the button from the behaviour, so it
    /// never promises a deletion that only unlists.
    /// </summary>
    public async Task<ScreenResult<PackageManageScreen>> DeleteAsync(ClaimsPrincipal caller, string? id, string? version, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "Delete", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        refusal = await RequireAsync(caller, id, AccessAction.Delete, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        // Read-only mode is refused for every handler at once, in EnterAsync, as the push endpoint's DELETE refuses before anything is touched.
        if (!NuGetVersion.TryParse(version, out var parsed)) return ScreenResult<PackageManageScreen>.NotFound();

        // False means the database held no such version - not that nothing happened: a hard delete removes the files either way.
        var found = await _deletions.TryDeletePackageAsync(id!, parsed, cancellationToken).ConfigureAwait(false);
        var normalized = parsed.ToNormalizedString();

        // PackageDeletionService notifies the catalog itself, so nothing is notified here.
        if (_services.Options.PackageDeletionBehavior == PackageDeletionBehavior.HardDelete)
        {
            return await DoneAsync(
                caller,
                id,
                found
                    ? $"{id} {normalized} is deleted. Its rows and its files are gone, and consumers "
                      + "can no longer restore it. Any indexed PDBs are still served, and an external "
                      + "search index keeps the version until it is reindexed."
                    : $"{id} {normalized} was not in the database. Any files left in storage for it were removed.",
                cancellationToken).ConfigureAwait(false);
        }

        return await DoneAsync(
            caller,
            id,
            found
                ? $"{id} {normalized} is unlisted. Deleting is configured to unlist on this server, so "
                  + "anyone who knows the exact version can still restore it."
                : $"There is no {id} {normalized} to unlist.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes one version's rows and files whatever the server's deletion behaviour is. Administrators only, and only after they confirm
    /// their password: it cannot be undone.
    /// </summary>
    public async Task<ScreenResult<PackageManageScreen>> HardDeleteAsync(ClaimsPrincipal caller, string? id, string? version, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "HardDelete", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        if (!_isAdministrator) return ScreenResult<PackageManageScreen>.Forbidden();
        if (!NuGetVersion.TryParse(version, out var parsed)) return ScreenResult<PackageManageScreen>.NotFound();

        var unconfirmed = await RefuseUnconfirmedAsync(caller, id, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        var found = await _deletions.TryHardDeletePackageAsync(id!, parsed, cancellationToken).ConfigureAwait(false);
        var normalized = parsed.ToNormalizedString();

        _logger.LogWarning("Administrator {Admin} permanently deleted {PackageId} {PackageVersion}", caller.Identity?.Name, id, normalized);

        return await DoneAsync(
            caller,
            id,
            found
                ? $"{id} {normalized} is deleted permanently. Its rows and its files are gone, and consumers "
                  + "can no longer restore it. Any indexed PDBs are still served, and an external "
                  + "search index keeps the version until it is reindexed."
                : $"{id} {normalized} was not in the database. Any files left in storage for it were removed.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes every version of the ID, then its owners, so the ID is free to claim again. Administrators only, and only after they confirm
    /// their password.
    /// </summary>
    public async Task<ScreenResult<PackageManageScreen>> HardDeleteAllAsync(ClaimsPrincipal caller, string? id, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "HardDeleteAll", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        if (!_isAdministrator) return ScreenResult<PackageManageScreen>.Forbidden();

        var unconfirmed = await RefuseUnconfirmedAsync(caller, id, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        var versions = await _packages.FindAsync(id!, includeUnlisted: true, cancellationToken).ConfigureAwait(false);
        foreach (var package in versions)
        {
            await _deletions.TryHardDeletePackageAsync(id!, package.Version, cancellationToken).ConfigureAwait(false);
        }

        var lowerId = id!.ToLowerInvariant();
        #pragma warning disable CS8602, CS8604
        _context.PackageOwners.RemoveRange(await _context.PackageOwners
            .Where(o => o.PackageId.ToLower() == lowerId)
            .ToListAsync(cancellationToken).ConfigureAwait(false));
        #pragma warning restore CS8602, CS8604
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogWarning(
            "Administrator {Admin} permanently deleted every version of {PackageId} ({Count}) and removed its owners",
            caller.Identity?.Name,
            id,
            versions.Count);

        return await DoneAsync(
            caller,
            id,
            $"Every version of {id} ({versions.Count}) is deleted permanently, and the ID has no owners.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Null when the signed in user confirmed with their password, otherwise the screen to show.</summary>
    private async Task<ScreenResult<PackageManageScreen>?> RefuseUnconfirmedAsync(ClaimsPrincipal caller, string? id, string? password, CancellationToken cancellationToken)
    {
        var current = await _services.Users.GetUserAsync(caller).ConfigureAwait(false);
        if (current == null) return ScreenResult<PackageManageScreen>.SignIn();

        return await _services.IsConfirmedAsync(current, password, cancellationToken).ConfigureAwait(false)
            ? null
            : await DoneAsync(caller, id, "The confirmation is incorrect, so nothing was deleted.", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deprecates a version.</summary>
    public async Task<ScreenResult<PackageManageScreen>> DeprecateAsync(
        ClaimsPrincipal caller,
        string? id,
        string? version,
        List<string>? reasons,
        string? message,
        string? alternatePackageId,
        string? alternateVersionRange,
        CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "Deprecate", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        refusal = await RequireAsync(caller, id, AccessAction.Annotate, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        reasons = reasons?.Where(r => PackageDeprecation.KnownReasons.Contains(r, StringComparer.Ordinal)).Distinct().ToList();
        if (reasons == null || reasons.Count == 0)
        {
            return await DoneAsync(caller, id, "Choose at least one deprecation reason.", cancellationToken).ConfigureAwait(false);
        }

        // The same rules as the metadata API (PackageMetadataAdminController.Validate).
        var alternateId = string.IsNullOrWhiteSpace(alternatePackageId) ? null : alternatePackageId.Trim();
        var alternateRange = string.IsNullOrWhiteSpace(alternateVersionRange) ? null : alternateVersionRange.Trim();
        if (alternateId == null && alternateRange != null)
        {
            return await DoneAsync(caller, id, "An alternate version range needs an alternate package ID.", cancellationToken).ConfigureAwait(false);
        }

        if (alternateRange != null && alternateRange != "*" && !VersionRange.TryParse(alternateRange, out _))
        {
            return await DoneAsync(caller, id, $"'{alternateRange}' is not a version range.", cancellationToken).ConfigureAwait(false);
        }

        var deprecation = new PackageDeprecation
        {
            Reasons = reasons.ToArray(),
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
            AlternatePackageId = alternateId,
            AlternateVersionRange = alternateRange,
        };

        return await ChangeVersionAsync(caller, id, version, v => _packages.SetDeprecationAsync(id!, v, deprecation, cancellationToken), "deprecated", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a version's deprecation.</summary>
    public async Task<ScreenResult<PackageManageScreen>> UndeprecateAsync(ClaimsPrincipal caller, string? id, string? version, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "Undeprecate", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        refusal = await RequireAsync(caller, id, AccessAction.Annotate, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        return await ChangeVersionAsync(caller, id, version, v => _packages.RemoveDeprecationAsync(id!, v, cancellationToken), "no longer deprecated", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds one vulnerability to a version, through the rules the metadata API applies (<see cref="PackageVulnerabilityRules"/>).</summary>
    public async Task<ScreenResult<PackageManageScreen>> AddVulnerabilityAsync(
        ClaimsPrincipal caller,
        string? id,
        string? version,
        string? advisoryUrl,
        int severity,
        string? versionRange,
        CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "AddVulnerability", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        refusal = await RequireAsync(caller, id, AccessAction.Annotate, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var added = new PackageVulnerability
        {
            AdvisoryUrl = advisoryUrl?.Trim(),
            Severity = severity,
            VersionRange = string.IsNullOrWhiteSpace(versionRange) ? null : versionRange.Trim(),
        };

        return await ChangeVulnerabilitiesAsync(caller, id, version, current => current.Append(added).ToList(), "updated (vulnerability added)", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes one vulnerability, identified by its key, from a version.</summary>
    public async Task<ScreenResult<PackageManageScreen>> RemoveVulnerabilityAsync(ClaimsPrincipal caller, string? id, string? version, int key, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "RemoveVulnerability", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        refusal = await RequireAsync(caller, id, AccessAction.Annotate, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        return await ChangeVulnerabilitiesAsync(caller, id, version, current => current.Where(v => v.Key != key).ToList(), "updated (vulnerability removed)", cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScreenResult<PackageManageScreen>> ChangeVulnerabilitiesAsync(
        ClaimsPrincipal caller,
        string? id,
        string? version,
        Func<IReadOnlyList<PackageVulnerability>, IReadOnlyList<PackageVulnerability>> change,
        string done,
        CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var parsed)) return ScreenResult<PackageManageScreen>.NotFound();

        var package = await _packages.FindOrNullAsync(id!, parsed, includeUnlisted: true, cancellationToken).ConfigureAwait(false);
        if (package == null) return ScreenResult<PackageManageScreen>.NotFound();

        // Fresh copies: the stored rows are replaced as a whole, so the list must not reuse tracked entities. Entries the vulnerability
        // scanner wrote (Source set) are not the administrator's to edit and are kept by the store, so they are left out of the list.
        var updated = change((package.Vulnerabilities ?? new List<PackageVulnerability>()).Where(v => v.Source == null).ToList())
            .Select(v => new PackageVulnerability
            {
                Key = v.Key,
                AdvisoryUrl = v.AdvisoryUrl,
                Severity = v.Severity,
                VersionRange = v.VersionRange,
            })
            .ToList();

        var error = PackageVulnerabilityRules.Validate(updated);
        if (error != null) return await DoneAsync(caller, id, error, cancellationToken).ConfigureAwait(false);

        foreach (var vulnerability in updated) vulnerability.Key = 0;

        return await ChangeVersionAsync(
            caller,
            id,
            version,
            v => _packages.SetVulnerabilitiesAsync(id!, v, updated, cancellationToken),
            done,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Marks or unmarks the ID as verified (reserved for its owners). Administrators only: owners cannot grant the flag to themselves.</summary>
    public async Task<ScreenResult<PackageManageScreen>> SetVerifiedAsync(ClaimsPrincipal caller, string? id, bool verified, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "SetVerified", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        if (!_isAdministrator) return ScreenResult<PackageManageScreen>.Forbidden();

        var changed = await _services.Ownership.SetVerifiedAsync(id!, verified, cancellationToken).ConfigureAwait(false);
        if (!changed) return ScreenResult<PackageManageScreen>.NotFound();

        _logger.LogWarning("Administrator {Admin} {Action} {PackageId}", caller.Identity?.Name, verified ? "verified" : "unverified", id);

        return await DoneAsync(caller, id, verified ? $"{id} is verified." : $"{id} is no longer verified.", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the PDBs recorded for a version. They stop being served; PDBs indexed before they were recorded cannot be deleted from here.</summary>
    public async Task<ScreenResult<PackageManageScreen>> DeleteSymbolsAsync(ClaimsPrincipal caller, string? id, string? version, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "DeleteSymbols", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        refusal = await RequireAsync(caller, id, AccessAction.Delete, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        if (!NuGetVersion.TryParse(version, out var parsed)) return ScreenResult<PackageManageScreen>.NotFound();

        var removed = await _symbols.DeleteAsync(id!, parsed, cancellationToken).ConfigureAwait(false);

        return await DoneAsync(
            caller,
            id,
            removed == 0
                ? $"{id} {parsed.ToNormalizedString()} has no recorded symbols to delete."
                : $"Deleted {removed} symbol file(s) of {id} {parsed.ToNormalizedString()}.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves an abuse report.</summary>
    public async Task<ScreenResult<PackageManageScreen>> ResolveReportAsync(ClaimsPrincipal caller, string? id, int key, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "ResolveReport", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        refusal = await RequireAsync(caller, id, AccessAction.Manage, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var lowerId = id!.ToLowerInvariant();
        #pragma warning disable CS8602, CS8604
        var report = await _context.AbuseReports.FirstOrDefaultAsync(r => r.Key == key && r.PackageId.ToLower() == lowerId, cancellationToken).ConfigureAwait(false);
        #pragma warning restore CS8602, CS8604
        if (report != null && report.Resolved == null)
        {
            report.Resolved = _time.UtcNow;
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await DoneAsync(caller, id, "The report is resolved.", cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScreenResult<PackageManageScreen>> ChangeVersionAsync(
        ClaimsPrincipal caller,
        string? id,
        string? version,
        Func<NuGetVersion, Task<bool>> change,
        string done,
        CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var parsed)) return ScreenResult<PackageManageScreen>.NotFound();

        bool changed;
        try
        {
            changed = await change(parsed).ConfigureAwait(false);
        }
        catch (NotSupportedException e)
        {
            return await DoneAsync(caller, id, e.Message, cancellationToken).ConfigureAwait(false);
        }

        if (!changed) return ScreenResult<PackageManageScreen>.NotFound();

        await _notifier.PackageChangedAsync(id!, parsed, cancellationToken).ConfigureAwait(false);

        return await DoneAsync(caller, id, $"{id} {parsed.ToNormalizedString()} is {done}.", cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScreenResult<PackageManageScreen>> DoneAsync(ClaimsPrincipal caller, string? id, string message, CancellationToken cancellationToken)
        => ScreenResult<PackageManageScreen>.Ok(await LoadAsync(caller, id, message, cancellationToken).ConfigureAwait(false));

    private async Task<PackageManageScreen> LoadAsync(ClaimsPrincipal caller, string? id, string? message, CancellationToken cancellationToken)
    {
        var screen = new PackageManageScreen
        {
            Id = id,
            Message = message,
            IsAdministrator = _isAdministrator,
            ActingAsAdministrator = _actingAsAdministrator,
            SymbolsSupported = _symbols.IsSupported,
            SeverityNames = PackageVulnerabilityRules.SeverityNames,
            CanReauthenticate = false, // set below from the caller's linked providers
            DeletionBehavior = _services.Options.PackageDeletionBehavior,
            IsReadOnlyMode = _services.IsReadOnlyMode,
        };

        if (_isAdministrator)
        {
            var confirmation = await _services.ConfirmationAsync(caller, cancellationToken).ConfigureAwait(false);
            screen.HasPassword = confirmation.HasPassword;
            screen.Reauthenticated = confirmation.Reauthenticated;
            screen.CanReauthenticate = confirmation.CanReauthenticate;
        }

        screen.Owners = (await _services.Ownership.GetOwnersAsync(id, cancellationToken).ConfigureAwait(false))
            .Select(o => new PackageManageOwner(o.OwnerName, o.IsVerified))
            .ToList();

        var versions = (await _packages.FindAsync(id!, includeUnlisted: true, cancellationToken).ConfigureAwait(false))
            .OrderByDescending(p => p.Version)
            .ToList();
        screen.Versions = versions
            .Select(p => new PackageManageVersion(
                p.Version.OriginalVersion ?? p.Version.ToNormalizedString(),
                p.NormalizedVersionString,
                p.Downloads,
                p.Listed,
                p.Deprecation == null
                    ? null
                    : new PackageManageDeprecation(p.Deprecation.Reasons ?? Array.Empty<string>(), p.Deprecation.Message, p.Deprecation.AlternatePackageId, p.Deprecation.AlternateVersionRange),
                (p.Vulnerabilities ?? new List<PackageVulnerability>())
                    .Select(v => new PackageManageVulnerability(v.Key, v.AdvisoryUrl, v.Severity, v.VersionRange, v.Source))
                    .ToList()))
            .ToList();

        if (_symbols.IsSupported)
        {
            var symbols = new Dictionary<string, IReadOnlyList<PackageManageSymbol>>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in versions)
            {
                var listed = await _symbols.ListAsync(id!, package.Version, cancellationToken).ConfigureAwait(false);
                if (listed.Count > 0)
                {
                    symbols[package.Version.ToNormalizedString()] = listed.Select(s => new PackageManageSymbol(s.FileName, s.Key)).ToList();
                }
            }

            screen.Symbols = symbols;
        }

        if (!(_context is NullContext))
        {
            var lowerId = id!.ToLowerInvariant();
            #pragma warning disable CS8602, CS8604
            screen.Reports = (await _context.AbuseReports
                    .AsNoTracking()
                    .Where(r => r.PackageId.ToLower() == lowerId && r.Resolved == null)
                    .OrderByDescending(r => r.Key)
                    .ToListAsync(cancellationToken).ConfigureAwait(false))
                .Select(r => new PackageManageReport(r.Key, r.PackageVersion, r.Reason, r.Details, r.ReporterEmail))
                .ToList();
            screen.HasValidation = await _context.PackageValidations
                .AsNoTracking()
                .AnyAsync(v => v.PackageId.ToLower() == lowerId, cancellationToken).ConfigureAwait(false);
            #pragma warning restore CS8602, CS8604
        }

        return screen;
    }

    private static string Describe(OwnerChangeResult result) => result switch
    {
        OwnerChangeResult.UnknownAccount => "There is no user or organization with that name.",
        OwnerChangeResult.AlreadyOwner => "That account is already an owner.",
        OwnerChangeResult.NotOwner => "That account is not an owner.",
        OwnerChangeResult.LastOwner => "A package must keep at least one owner.",
        _ => result.ToString(),
    };
}
