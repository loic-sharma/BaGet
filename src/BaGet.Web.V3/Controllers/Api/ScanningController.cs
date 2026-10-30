using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.Models.Api;
using BaGet.Web.V3.Api;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Controllers.Api;

/// <summary>
/// Vulnerability scanning: the Scanning, ScanRepository and ScanProject pages' operations. Saving a policy asks for the owner's password
/// (it decides who may download and deploy what); starting a scan does not and runs as the <c>vulnerability-scan</c> operation.
/// </summary>
[Route(ApiCatalog.RootTemplate + "/scanning")]
public class ScanningController : AccessApiController
{
    /// <summary>The overview.</summary>
    [HttpGet]
    [ApiOperation("Show whether scanning is on and every repository's numbers and policy", "AdminScanning", "Index", Response = typeof(ScanOverviewDto))]
    public async Task<IActionResult> Overview()
    {
        var options = Service<IOptionsSnapshot<ScanningOptions>>().Value;
        var rows = new List<ScanRepositoryDto>();
        foreach (var repository in (await Service<IRepositoryService>().ListAsync(Cancellation)).Where(r => r.Kind != RepositoryKind.Virtual))
        {
            rows.Add(await RowAsync(repository));
        }

        return Respond(new ScanOverviewDto(options.Enabled, Service<ITrivyRunner>().IsConfigured(options), rows));
    }

    /// <summary>One repository's dashboard.</summary>
    [HttpGet("repositories/{name}")]
    [ApiOperation("Show a repository's scan numbers, its own policy and the last scan", "AdminScanning", "Repository", Response = typeof(ScanDashboardDto))]
    public async Task<IActionResult> Repository(string name)
    {
        var repository = await FindAsync(name);
        if (repository == null) return ApiProblems.NotFound($"scannable repository '{name}'");

        var own = await Service<IScanPolicyService>().GetAsync(ScanPolicyScope.Repository, repository.Key, Cancellation);

        return Respond(new ScanDashboardDto(await RowAsync(repository), own, Service<AdminJobService>().Get("vulnerability-scan", repository.Name)));
    }

    /// <summary>The versions with findings.</summary>
    [HttpGet("repositories/{name}/results")]
    [ApiOperation("List the scanned versions with findings of at least a severity, worst first", "AdminScanning", "Repository", Response = typeof(ScanResult), PagedList = true)]
    public async Task<IActionResult> Results(string name, int? min, int? page, int? pageSize)
    {
        var repository = await FindAsync(name);
        if (repository == null) return ApiProblems.NotFound($"scannable repository '{name}'");

        var (number, size) = ApiProblems.Clamp(page, pageSize);
        using var scope = Service<IServiceScopeFactory>().CreateRepositoryScope(repository);
        var scans = scope.ServiceProvider.GetRequiredService<IScanService>();
        var results = await scans.ListAsync(Math.Clamp(min ?? 0, 0, PackageVulnerabilityRules.MaxSeverity), (number - 1) * size, size + 1, Cancellation);
        // The list is read one past the page: "total" is exact on the last page and a lower bound (the count so far) while more follow.
        var total = (number - 1) * size + results.Count;

        return Respond(new Paged<ScanResult>(results.Take(size).ToList(), number, size, total));
    }

    /// <summary>Saves a repository's policy.</summary>
    [HttpPut("repositories/{name}/policy")]
    [ApiOperation("Set a repository's scanning policy (all thresholds empty removes it)", "AdminScanning", "Repository", Confirm = true, Response = typeof(EffectiveScanPolicy))]
    public async Task<IActionResult> SetPolicy(string name, [FromBody] ScanPolicyRequest request)
    {
        ThrowIfClosed();
        var repository = await FindAsync(name);
        if (repository == null) return ApiProblems.NotFound($"scannable repository '{name}'");
        var values = request.ToValues();
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        var policies = Service<IScanPolicyService>();
        await policies.SaveAsync(ScanPolicyScope.Repository, repository.Key, values, Actor, Cancellation);

        return Respond(await policies.GetEffectiveAsync(repository.Key, Cancellation));
    }

    /// <summary>Starts a scan.</summary>
    [HttpPost("repositories/{name}/scan")]
    [ApiOperation("Scan every artifact of a repository in the background", "AdminScanning", "Repository", Response = typeof(AdminJobStatus), Status = 202)]
    public async Task<IActionResult> Scan(string name)
    {
        ThrowIfClosed();
        var repository = await FindAsync(name);
        if (repository == null) return ApiProblems.NotFound($"scannable repository '{name}'");

        var jobs = Service<AdminJobService>();
        var operation = jobs.Operations.FirstOrDefault(o => o.Name == "vulnerability-scan");
        if (operation == null || !operation.IsAvailable(HttpContext.RequestServices) || !operation.AppliesTo(repository))
        {
            return ApiProblems.Conflict("Scanning is not available: it is turned off (Scanning:Enabled) or the repository cannot be scanned.");
        }

        if (!jobs.TryStart(operation.Name, repository, Actor, out var refusal)) return ApiProblems.Conflict(refusal ?? "The scan was not started.");

        return Respond(jobs.Get(operation.Name, repository.Name), 202, $"{ApiCatalog.Root}/operations/{operation.Name}/repositories/{Uri.EscapeDataString(repository.Name)}");
    }

    /// <summary>A project's policy.</summary>
    [HttpGet("projects/{name}/policy")]
    [ApiOperation("Show a project's scanning policy", "AdminScanning", "Project", Response = typeof(ScanPolicy))]
    public async Task<IActionResult> ProjectPolicy(string name)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        var project = await AccessAdmin.GetProjectAsync(name, Cancellation);
        if (project == null) return ApiProblems.NotFound($"project '{name}'");

        return await Service<IScanPolicyService>().GetAsync(ScanPolicyScope.Project, project.Project.Key, Cancellation) is { } policy
            ? Respond(policy)
            : ApiProblems.NotFound($"scanning policy of project '{name}'");
    }

    /// <summary>Saves a project's policy.</summary>
    [HttpPut("projects/{name}/policy")]
    [ApiOperation("Set a project's scanning policy (all thresholds empty removes it)", "AdminScanning", "Project", Confirm = true, Status = 204)]
    public async Task<IActionResult> SetProjectPolicy(string name, [FromBody] ScanPolicyRequest request)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        var project = await AccessAdmin.GetProjectAsync(name, Cancellation);
        if (project == null) return ApiProblems.NotFound($"project '{name}'");
        var values = request.ToValues();
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await Service<IScanPolicyService>().SaveAsync(ScanPolicyScope.Project, project.Project.Key, values, Actor, Cancellation);

        return Done();
    }

    private async Task<RepositoryInfo?> FindAsync(string name)
    {
        var repository = await Service<IRepositoryService>().FindAsync(name, Cancellation);

        return repository == null || repository.Kind == RepositoryKind.Virtual ? null : repository;
    }

    private async Task<ScanRepositoryDto> RowAsync(RepositoryInfo repository)
    {
        using var scope = Service<IServiceScopeFactory>().CreateRepositoryScope(repository);
        var summary = await scope.ServiceProvider.GetRequiredService<IScanService>().SummarizeAsync(Cancellation);

        return new ScanRepositoryDto(repository.Name, summary, await Service<IScanPolicyService>().GetEffectiveAsync(repository.Key, Cancellation));
    }
}
