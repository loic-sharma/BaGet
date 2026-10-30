using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using BaGet.Identity.Application.Models.Operations;
using BaGet.Identity.Application.Models.Repositories;
using BaGet.Identity.Application.Models.Settings;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>
/// The operational screens of the administrator: maintenance operations (catalog rebuild, downloads import, Docker garbage
/// collection) started as background jobs for a chosen repository (503 while the <see cref="MaintenanceGate"/> is closed, recorded
/// with <see cref="IAdminAudit"/>); the validation queue across every repository (retry, release, reject); the abuse reports of
/// every repository (resolve, reopen); and the read-only formats, cluster status and "access control unavailable" notice.
/// </summary>
public sealed class AdminOperationsUseCase : AdminUseCaseBase
{
    private const string NotConfirmedToStart = "The confirmation is incorrect, so nothing was started.";
    private static readonly ScreenRef OperationsScreen = ScreenCatalog.AdminOperations;
    private static readonly ScreenRef ValidationScreen = ScreenCatalog.AdminValidation;
    private static readonly ScreenRef AbuseScreen = ScreenCatalog.AdminAbuse;

    private readonly IServiceProvider _services;
    private readonly AdminJobService _jobs;
    private readonly IRepositoryService _repositories;
    private readonly IRepositoryAdministration _repositoryAdministration;
    private readonly IAbuseReportAdministration _reports;
    private readonly IAdminAudit _audit;
    private readonly FormatRegistry _formats;
    private readonly ReadinessService _readiness;
    private readonly IClusterStatusService _cluster;
    private readonly IValidationAdministration? _validation;

    /// <summary>Creates the use case.</summary>
    public AdminOperationsUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IServiceProvider services,
        AdminJobService jobs,
        IRepositoryService repositories,
        IRepositoryAdministration repositoryAdministration,
        IAbuseReportAdministration reports,
        IAdminAudit audit,
        FormatRegistry formats,
        ReadinessService readiness,
        IClusterStatusService cluster,
        MaintenanceGate? gate = null,
        IValidationAdministration? validation = null)
        : base(users, confirmation, gate)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _repositoryAdministration = repositoryAdministration ?? throw new ArgumentNullException(nameof(repositoryAdministration));
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _formats = formats ?? throw new ArgumentNullException(nameof(formats));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
        _validation = validation;
    }

    // ------------------------------------------------------------------ operations

    /// <summary>Lists the operations and their status. <paramref name="returnPage"/> is the address to come back to after signing in again.</summary>
    public async Task<ScreenResult<OperationsViewModel>> GetOperationsAsync(ClaimsPrincipal caller, string? returnPage, CancellationToken cancellationToken)
    {
        var model = new OperationsViewModel();
        await Confirmation.LoadAsync(model, caller, returnPage);

        var repositories = await _repositories.ListAsync(cancellationToken);

        model.Rows = _jobs.Operations
            .Where(o => o.IsAvailable(_services))
            .SelectMany(o => repositories.Where(o.AppliesTo).Select(r => new OperationRow(o, r, _jobs.Get(o.Name, r.Name))))
            .ToList();

        return ScreenResult<OperationsViewModel>.Ok(model);
    }

    /// <summary>Starts <paramref name="operation"/> on <paramref name="repository"/>.</summary>
    public async Task<ScreenResult<OperationsViewModel>> StartAsync(ClaimsPrincipal caller, string? operation, string? repository, string? password, CancellationToken cancellationToken)
    {
        var info = await _repositories.FindAsync(repository, cancellationToken);
        var definition = _jobs.Operations.FirstOrDefault(o => o.Name == operation);
        if (info == null || definition == null || !definition.IsAvailable(_services) || !definition.AppliesTo(info)) return ScreenResult<OperationsViewModel>.NotFound();

        var user = await CurrentAsync(caller);

        // An operation that deletes data asks for the password again, like the other destructive pages.
        if (definition.RequiresConfirmation)
        {
            if (user == null) return ScreenResult<OperationsViewModel>.SignIn();

            if (!await Confirmation.IsConfirmedAsync(user, password))
            {
                return Back<OperationsViewModel>(OperationsScreen, null, null, NotConfirmedToStart);
            }
        }

        return _jobs.TryStart(definition.Name, info, user?.UserName, out var refusal)
            ? Back<OperationsViewModel>(OperationsScreen, null, $"{definition.Title} started on {info.Name}.", null)
            : Back<OperationsViewModel>(OperationsScreen, null, null, refusal);
    }

    // ------------------------------------------------------------------ validation

    /// <summary>Lists the submissions.</summary>
    public async Task<ScreenResult<ValidationViewModel>> GetValidationAsync(ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        var model = new ValidationViewModel { IsSupported = _validation?.IsSupported == true };
        if (model.IsSupported)
        {
            model.Submissions = await _validation!.ListAsync(cancellationToken);
        }

        await Confirmation.LoadAsync(model, caller);

        return ScreenResult<ValidationViewModel>.Ok(model);
    }

    /// <summary>Validates the submission again.</summary>
    public Task<ScreenResult<ValidationViewModel>> RetryAsync(ClaimsPrincipal caller, string? submissionId, CancellationToken cancellationToken)
        => ValidationActAsync(caller, submissionId, null, confirm: false, (a, id, user) => a.RetryAsync(id, user, cancellationToken), "queued for validation again");

    /// <summary>Indexes the quarantined package although validation failed.</summary>
    public Task<ScreenResult<ValidationViewModel>> ReleaseAsync(ClaimsPrincipal caller, string? submissionId, string? password, CancellationToken cancellationToken)
        => ValidationActAsync(caller, submissionId, password, confirm: true, (a, id, user) => a.ReleaseAsync(id, user, cancellationToken), "released and indexed");

    /// <summary>Deletes the quarantined package.</summary>
    public Task<ScreenResult<ValidationViewModel>> RejectAsync(ClaimsPrincipal caller, string? submissionId, string? password, CancellationToken cancellationToken)
        => ValidationActAsync(caller, submissionId, password, confirm: true, (a, id, user) => a.RejectAsync(id, user, cancellationToken), "rejected and deleted");

    private async Task<ScreenResult<ValidationViewModel>> ValidationActAsync(
        ClaimsPrincipal caller,
        string? submissionId,
        string? password,
        bool confirm,
        Func<IValidationAdministration, string, string?, Task<string?>> action,
        string done)
    {
        var administration = _validation;
        if (administration == null || !administration.IsSupported || string.IsNullOrEmpty(submissionId)) return ScreenResult<ValidationViewModel>.NotFound();

        var user = await CurrentAsync(caller);
        if (user == null) return ScreenResult<ValidationViewModel>.SignIn();

        if (confirm && !await Confirmation.IsConfirmedAsync(user, password))
        {
            return Back<ValidationViewModel>(ValidationScreen, null, null, NotConfirmed);
        }

        var refusal = await action(administration, submissionId, user.UserName);

        return refusal == null
            ? Back<ValidationViewModel>(ValidationScreen, null, $"Submission {submissionId} was {done}.", null)
            : Back<ValidationViewModel>(ValidationScreen, null, null, refusal);
    }

    // ------------------------------------------------------------------ abuse reports

    /// <summary>Lists the reports (the unresolved ones unless <paramref name="all"/>).</summary>
    public async Task<ScreenResult<AbuseViewModel>> GetAbuseAsync(bool all, CancellationToken cancellationToken)
        => ScreenResult<AbuseViewModel>.Ok(new AbuseViewModel
        {
            All = all,
            IsSupported = _reports.IsSupported,
            Items = await _reports.ListAsync(unresolvedOnly: !all, cancellationToken),
        });

    /// <summary>Marks a report resolved.</summary>
    public Task<ScreenResult<AbuseViewModel>> ResolveAsync(ClaimsPrincipal caller, int key, bool all, CancellationToken cancellationToken)
        => SetResolvedAsync(caller, key, all, true, cancellationToken);

    /// <summary>Reopens a resolved report.</summary>
    public Task<ScreenResult<AbuseViewModel>> ReopenAsync(ClaimsPrincipal caller, int key, bool all, CancellationToken cancellationToken)
        => SetResolvedAsync(caller, key, all, false, cancellationToken);

    private async Task<ScreenResult<AbuseViewModel>> SetResolvedAsync(ClaimsPrincipal caller, int key, bool all, bool resolved, CancellationToken cancellationToken)
    {
        if (!await _reports.SetResolvedAsync(key, resolved, cancellationToken)) return ScreenResult<AbuseViewModel>.NotFound();

        var user = await CurrentAsync(caller);
        _audit.Record(user?.UserName, resolved ? "abuse.resolve" : "abuse.reopen", $"report {key}");

        return Back<AbuseViewModel>(
            AbuseScreen,
            Route(("all", all ? "true" : null)),
            resolved ? $"Report {key} was resolved." : $"Report {key} was reopened.",
            null);
    }

    // ------------------------------------------------------------------ formats, cluster and the access notice

    /// <summary>Lists the formats.</summary>
    public async Task<ScreenResult<FormatsViewModel>> GetFormatsAsync(CancellationToken cancellationToken)
    {
        var repositories = await _repositoryAdministration.ListAsync(cancellationToken);

        return ScreenResult<FormatsViewModel>.Ok(new FormatsViewModel
        {
            Rows = _formats.Modules
                .Select(module => new FormatRow(
                    module,
                    repositories.Count(r => r.Format == module.Format),
                    Capabilities(_services, module.Format)))
                .ToList(),
        });
    }

    // What the module's services answer to: a capability a phase has not implemented yet is simply not listed.
    private static IReadOnlyList<string> Capabilities(IServiceProvider services, RepositoryFormat format)
    {
        var list = new List<string>();
        if (services.GetFormatService<IFormatIndexer>(format) != null) list.Add("Deploy (indexing)");
        if (services.GetFormatService<IFormatSearchProvider>(format) != null) list.Add("Search");
        if (services.GetFormatService<IFormatRetentionHooks>(format) != null) list.Add("Retention");
        if (services.GetFormatService<IFormatScanner>(format) != null) list.Add("Vulnerability scanning");

        return list;
    }

    /// <summary>Reads the cluster status; <paramref name="refresh"/> runs the health checks again instead of using the last answer.</summary>
    public async Task<ScreenResult<ClusterViewModel>> GetClusterAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (refresh) await _readiness.CheckAsync(force: true, cancellationToken);

        return ScreenResult<ClusterViewModel>.Ok(new ClusterViewModel { Status = await _cluster.GetAsync(cancellationToken) });
    }

    /// <summary>The notice shown where the configured database has no access model.</summary>
    public ScreenResult<AccessUnavailableViewModel> GetAccessUnavailable()
        => ScreenResult<AccessUnavailableViewModel>.Ok(AccessUnavailableViewModel.Standard);
}
