using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Models.Policies;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.UseCases.Policies;

/// <summary>
/// The vulnerability scanning screens: the overview of every repository and project, the dashboard of one repository (its findings, its
/// policy, <em>Scan now</em>) and the policy of a project. Saving a policy asks for the administrator's password; starting a scan does not.
/// Every write is refused while the server is read-only. The links to the package screens of the findings are <see cref="ScreenLink"/>s: the
/// use case knows no routes.
/// </summary>
public sealed class ScanningUseCase : PolicyUseCaseBase
{
    private static readonly ScreenRef RepositoryScreen = ScreenCatalog.AdminScanRepository;
    private static readonly ScreenRef ProjectScreen = ScreenCatalog.AdminScanProject;

    private readonly IServiceProvider _services;
    private readonly IOptionsSnapshot<ScanningOptions> _options;
    private readonly ITrivyRunner _trivy;
    private readonly IServiceScopeFactory _scopes;
    private readonly IRepositoryService _repositories;
    private readonly IAccessAdministration _access;
    private readonly IScanPolicyService _policies;
    private readonly AdminJobService _jobs;

    /// <summary>Creates the use case.</summary>
    public ScanningUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IServiceProvider services,
        IOptionsSnapshot<ScanningOptions> options,
        ITrivyRunner trivy,
        IServiceScopeFactory scopes,
        IRepositoryService repositories,
        IAccessAdministration access,
        IScanPolicyService policies,
        AdminJobService jobs,
        MaintenanceGate? gate = null)
        : base(users, confirmation, gate)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _trivy = trivy ?? throw new ArgumentNullException(nameof(trivy));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    // ------------------------------------------------------------------ overview

    /// <summary>Shows the overview.</summary>
    public async Task<ScreenResult<ScanningViewModel>> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var model = new ScanningViewModel
        {
            Options = options,
            Settings = new ScanningSettingsView(options.Enabled, options.ScanOnDeploy, options.OsvUrl, options.OsvOfflinePath, options.RescanInterval),
            TrivyConfigured = _trivy.IsConfigured(options),
        };

        var rows = new List<ScanningRepositoryRow>();
        foreach (var repository in (await _repositories.ListAsync(cancellationToken)).Where(r => r.Kind != RepositoryKind.Virtual))
        {
            using var scope = _scopes.CreateRepositoryScope(repository);
            var summary = await scope.ServiceProvider.GetRequiredService<IScanService>().SummarizeAsync(cancellationToken);
            rows.Add(new ScanningRepositoryRow(repository, summary, await _policies.GetEffectiveAsync(repository.Key, cancellationToken)));
        }

        model.Repositories = rows;

        var projects = new List<ScanningProjectRow>();
        foreach (var project in await _access.ListProjectsAsync(cancellationToken))
        {
            projects.Add(new ScanningProjectRow(project, await _policies.GetAsync(ScanPolicyScope.Project, project.Key, cancellationToken) != null));
        }

        model.Projects = projects;

        return ScreenResult<ScanningViewModel>.Ok(model);
    }

    // ------------------------------------------------------------------ one repository

    /// <summary>Shows the dashboard of a repository: its findings from <paramref name="minSeverity"/> up, <paramref name="pageNumber"/> (from 1; 0 is the first).</summary>
    public async Task<ScreenResult<ScanRepositoryViewModel>> GetRepositoryAsync(
        ClaimsPrincipal caller, string? name, int minSeverity, int pageNumber, string? returnPage, CancellationToken cancellationToken)
    {
        var model = new ScanRepositoryViewModel { Name = name, MinSeverity = minSeverity, PageNumber = pageNumber == 0 ? 1 : pageNumber };
        if (!await LoadRepositoryAsync(model, cancellationToken)) return ScreenResult<ScanRepositoryViewModel>.NotFound();

        await Confirmation.LoadAsync(model, caller, returnPage);

        return ScreenResult<ScanRepositoryViewModel>.Ok(model);
    }

    /// <summary>Saves the repository policy; empty values remove it.</summary>
    public async Task<ScreenResult<ScanRepositoryViewModel>> SaveRepositoryPolicyAsync(
        ClaimsPrincipal caller, string? name, ScanPolicyBody? body, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        var repository = await FindRepositoryAsync(name, cancellationToken);
        if (repository == null) return ScreenResult<ScanRepositoryViewModel>.NotFound();

        return await ActAsync<ScanRepositoryViewModel>(caller, body?.Password, confirm: true, async () =>
        {
            var values = (body ?? new ScanPolicyBody(null, null, null, false, null)).ToFields().ToValues();
            await _policies.SaveAsync(ScanPolicyScope.Repository, repository.Key, values, ActorOf(caller), cancellationToken);

            return values.IsEmpty ? $"The scanning policy of {repository.Name} was removed." : $"The scanning policy of {repository.Name} was saved.";
        }, RepositoryScreen, Route(("name", name)));
    }

    /// <summary>Starts a scan of every artifact of the repository in the background.</summary>
    public async Task<ScreenResult<ScanRepositoryViewModel>> ScanAsync(ClaimsPrincipal caller, string? name, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        var repository = await FindRepositoryAsync(name, cancellationToken);
        if (repository == null) return ScreenResult<ScanRepositoryViewModel>.NotFound();

        var route = Route(("name", name));
        var operation = _jobs.Operations.FirstOrDefault(o => o.Name == "vulnerability-scan");
        if (operation == null || !operation.IsAvailable(_services) || !operation.AppliesTo(repository))
        {
            return Back<ScanRepositoryViewModel>(
                RepositoryScreen, route, null, "Scanning is not available: it is turned off (Scanning:Enabled) or the repository cannot be scanned.");
        }

        return _jobs.TryStart(operation.Name, repository, ActorOf(caller), out var refusal)
            ? Back<ScanRepositoryViewModel>(RepositoryScreen, route, $"A scan of {repository.Name} started. Its status is shown below and on the Operations page.", null)
            : Back<ScanRepositoryViewModel>(RepositoryScreen, route, null, refusal);
    }

    private async Task<RepositoryInfo?> FindRepositoryAsync(string? name, CancellationToken cancellationToken)
    {
        var repository = await _repositories.FindAsync(name, cancellationToken);

        return repository == null || repository.Kind == RepositoryKind.Virtual ? null : repository;
    }

    private async Task<bool> LoadRepositoryAsync(ScanRepositoryViewModel model, CancellationToken cancellationToken)
    {
        model.Repository = await FindRepositoryAsync(model.Name, cancellationToken);
        if (model.Repository == null) return false;

        model.Own = await _policies.GetAsync(ScanPolicyScope.Repository, model.Repository.Key, cancellationToken);
        model.Effective = await _policies.GetEffectiveAsync(model.Repository.Key, cancellationToken);
        model.Job = _jobs.Get("vulnerability-scan", model.Repository.Name);

        using var scope = _scopes.CreateRepositoryScope(model.Repository);
        var scans = scope.ServiceProvider.GetRequiredService<IScanService>();
        model.Summary = await scans.SummarizeAsync(cancellationToken);
        var page = Math.Max(1, model.PageNumber);
        var size = ScanRepositoryViewModel.PageSize;
        var results = await scans.ListAsync(Math.Clamp(model.MinSeverity, 0, PackageVulnerabilityRules.MaxSeverity), (page - 1) * size, size + 1, cancellationToken);
        model.HasMore = results.Count > size;
        model.Findings = results.Take(size).ToList();

        var links = new Dictionary<int, ScreenLink>();
        foreach (var result in model.Findings)
        {
            if (PackageScreen(model.Repository, result) is { } link) links[result.Key] = link;
        }

        model.PackageLinks = links;

        return true;
    }

    // The package screen of a version in its format (the repository's package page); null where there is none.
    private static ScreenLink? PackageScreen(RepositoryInfo info, ScanResult result)
    {
        var repo = info.IsDefault ? null : info.Name;
        var name = result.Name ?? string.Empty;
        var version = result.Version ?? string.Empty;

        switch (info.Format)
        {
            case RepositoryFormat.NuGet:
                return new ScreenLink(ScreenCatalog.Package, Route(("id", name), ("version", version), ("repo", repo)));
            case RepositoryFormat.Npm:
                return new ScreenLink(ScreenCatalog.NpmPackage, Route(("id", name), ("version", version), ("repo", repo)));
            case RepositoryFormat.Maven:
                return MavenNames.TryParseArtifactName(name, out var group, out var artifact)
                    ? new ScreenLink(ScreenCatalog.MavenArtifact, Route(("group", group), ("artifact", artifact), ("version", version), ("repo", repo)))
                    : null;
            case RepositoryFormat.Docker:
                return new ScreenLink(ScreenCatalog.DockerImage, Route(("image", name), ("digest", version), ("repo", repo)));
            default:
                return null;
        }
    }

    // ------------------------------------------------------------------ one project

    /// <summary>Shows the policy of the project.</summary>
    public async Task<ScreenResult<ScanProjectViewModel>> GetProjectAsync(ClaimsPrincipal caller, string? name, string? returnPage, CancellationToken cancellationToken)
    {
        var model = new ScanProjectViewModel { Name = name };
        model.Project = await _access.GetProjectAsync(name ?? string.Empty, cancellationToken);
        if (model.Project == null) return ScreenResult<ScanProjectViewModel>.NotFound();

        model.Own = await _policies.GetAsync(ScanPolicyScope.Project, model.Project.Project.Key, cancellationToken);
        await Confirmation.LoadAsync(model, caller, returnPage);

        return ScreenResult<ScanProjectViewModel>.Ok(model);
    }

    /// <summary>Saves the project policy; empty values remove it.</summary>
    public async Task<ScreenResult<ScanProjectViewModel>> SaveProjectPolicyAsync(
        ClaimsPrincipal caller, string? name, ScanPolicyBody? body, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        var project = await _access.GetProjectAsync(name ?? string.Empty, cancellationToken);
        if (project == null) return ScreenResult<ScanProjectViewModel>.NotFound();

        return await ActAsync<ScanProjectViewModel>(caller, body?.Password, confirm: true, async () =>
        {
            var values = (body ?? new ScanPolicyBody(null, null, null, false, null)).ToFields().ToValues();
            await _policies.SaveAsync(ScanPolicyScope.Project, project.Project.Key, values, ActorOf(caller), cancellationToken);

            return values.IsEmpty ? $"The scanning policy of {project.Project.Name} was removed." : $"The scanning policy of {project.Project.Name} was saved.";
        }, ProjectScreen, Route(("name", name)));
    }
}
