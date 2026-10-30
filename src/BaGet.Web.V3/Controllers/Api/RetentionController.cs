using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.Models.Api;
using BaGet.Web.V3.Api;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Controllers.Api;

/// <summary>
/// Retention policies, their runs and legal holds: the Retention, RetentionPolicy and RetentionHolds pages' operations. Creating,
/// changing, running and deleting a policy, and lifting a hold, ask for the owner's password as the pages do; a dry run and placing a hold
/// do not.
/// </summary>
[Route(ApiCatalog.RootTemplate + "/retention")]
public class RetentionController : AdminApiController
{
    private IRetentionAdministration Retention => Service<IRetentionAdministration>();

    private RetentionJobs Jobs => Service<RetentionJobs>();

    /// <summary>Lists policies.</summary>
    [HttpGet("policies")]
    [ApiOperation("List retention policies with their last run", "AdminRetention", "Index", Response = typeof(RetentionPolicyDto), PagedList = true)]
    public async Task<IActionResult> List(int? page, int? pageSize)
        => RespondPage((await Retention.ListPoliciesAsync(Cancellation)).Select(p => new RetentionPolicyDto(p, Jobs.Get(p.Policy.Key))).ToList(), page, pageSize);

    /// <summary>One policy.</summary>
    [HttpGet("policies/{key:int}")]
    [ApiOperation("Show a retention policy with its last run and report", "AdminRetention", "Policy", Response = typeof(RetentionPolicyDto))]
    public async Task<IActionResult> Get(int key)
        => await Retention.GetPolicyAsync(key, Cancellation) is { } policy ? Respond(new RetentionPolicyDto(policy, Jobs.Get(key))) : ApiProblems.NotFound($"retention policy {key}");

    /// <summary>Creates a policy.</summary>
    [HttpPost("policies")]
    [ApiOperation("Create a retention policy", "AdminRetention", "Index", Confirm = true, Response = typeof(RetentionPolicyDto), Status = 201)]
    public async Task<IActionResult> Create([FromBody] RetentionPolicyRequest request)
    {
        ThrowIfClosed();
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        var policy = await Retention.SavePolicyAsync(request.ToInput(0), Actor, Cancellation);

        return Respond(new RetentionPolicyDto((await Retention.GetPolicyAsync(policy.Key, Cancellation))!, null), 201, $"{ApiCatalog.Root}/retention/policies/{policy.Key}");
    }

    /// <summary>Replaces a policy's rules.</summary>
    [HttpPut("policies/{key:int}")]
    [ApiOperation("Replace a retention policy's scope and rules", "AdminRetention", "Policy", Confirm = true, Response = typeof(RetentionPolicyDto))]
    public async Task<IActionResult> Update(int key, [FromBody] RetentionPolicyRequest request)
    {
        ThrowIfClosed();
        if (await Retention.GetPolicyAsync(key, Cancellation) == null) return ApiProblems.NotFound($"retention policy {key}");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await Retention.SavePolicyAsync(request.ToInput(key), Actor, Cancellation);

        return Respond(new RetentionPolicyDto((await Retention.GetPolicyAsync(key, Cancellation))!, Jobs.Get(key)));
    }

    /// <summary>Deletes a policy.</summary>
    [HttpDelete("policies/{key:int}")]
    [ApiOperation("Delete a retention policy (what it deleted stays deleted)", "AdminRetention", "Policy", Confirm = true, Status = 204)]
    public async Task<IActionResult> Delete(int key)
    {
        ThrowIfClosed();
        if (await Retention.GetPolicyAsync(key, Cancellation) == null) return ApiProblems.NotFound($"retention policy {key}");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await Retention.DeletePolicyAsync(key, Actor, Cancellation);

        return Done();
    }

    /// <summary>Starts a dry run.</summary>
    [HttpPost("policies/{key:int}/dry-run")]
    [ApiOperation("Start a dry run of a policy: the report of what a run would delete (nothing is deleted)", "AdminRetention", "Policy", Response = typeof(RetentionJobStatus), Status = 202)]
    public async Task<IActionResult> DryRun(int key)
    {
        if (await Retention.GetPolicyAsync(key, Cancellation) == null) return ApiProblems.NotFound($"retention policy {key}");
        if (!Jobs.TryStart(key, dryRun: true, Actor, out var refusal)) return ApiProblems.Conflict(refusal ?? "The dry run was not started.");

        return Respond(Jobs.Get(key), 202, $"{ApiCatalog.Root}/retention/policies/{key}/run");
    }

    /// <summary>Starts a run that deletes.</summary>
    [HttpPost("policies/{key:int}/run")]
    [ApiOperation("Start a run of a policy: the versions it selects are deleted", "AdminRetention", "Policy", Confirm = true, Response = typeof(RetentionJobStatus), Status = 202)]
    public async Task<IActionResult> Run(int key)
    {
        ThrowIfClosed();
        var view = await Retention.GetPolicyAsync(key, Cancellation);
        if (view == null) return ApiProblems.NotFound($"retention policy {key}");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;
        if (!view.Policy.Enabled) return ApiProblems.Conflict("The policy is disabled: enable it first (a dry run works either way).");
        if (!Jobs.TryStart(key, dryRun: false, Actor, out var problem)) return ApiProblems.Conflict(problem ?? "The run was not started.");

        return Respond(Jobs.Get(key), 202, $"{ApiCatalog.Root}/retention/policies/{key}/run");
    }

    /// <summary>The last run of a policy.</summary>
    [HttpGet("policies/{key:int}/run")]
    [ApiOperation("Show the last run (or dry run) of a policy with its report", "AdminRetention", "Policy", Response = typeof(RetentionJobStatus))]
    public async Task<IActionResult> LastRun(int key)
    {
        if (await Retention.GetPolicyAsync(key, Cancellation) == null) return ApiProblems.NotFound($"retention policy {key}");

        return Jobs.Get(key) is { } status ? Respond(status) : ApiProblems.NotFound($"run of retention policy {key} since the server started");
    }

    /// <summary>Lists legal holds.</summary>
    [HttpGet("holds")]
    [ApiOperation("List legal holds, optionally of one repository", "AdminRetention", "Holds", Response = typeof(RetentionHoldView), PagedList = true)]
    public async Task<IActionResult> Holds(string? repository, int? page, int? pageSize)
        => RespondPage(await Retention.ListHoldsAsync(string.IsNullOrWhiteSpace(repository) ? null : repository, Cancellation), page, pageSize);

    /// <summary>Places a hold.</summary>
    [HttpPost("holds")]
    [ApiOperation("Place a legal hold on an artifact (retention never deletes it)", "AdminRetention", "Holds", Response = typeof(RetentionHold), Status = 201)]
    public async Task<IActionResult> AddHold([FromBody] HoldRequest request)
    {
        ThrowIfClosed();
        var hold = await Retention.AddHoldAsync(request.Repository ?? string.Empty, request.Name ?? string.Empty, request.Version, request.Reason, Actor, Cancellation);

        return Respond(hold, 201);
    }

    /// <summary>Lifts a hold.</summary>
    [HttpDelete("holds/{key:int}")]
    [ApiOperation("Lift a legal hold", "AdminRetention", "Holds", Confirm = true, Status = 204)]
    public async Task<IActionResult> RemoveHold(int key)
    {
        ThrowIfClosed();
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        return await Retention.RemoveHoldAsync(key, Actor, Cancellation) ? Done() : ApiProblems.NotFound($"hold {key}");
    }
}
