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
/// Replication: the Replication and ReplicationConfig pages' operations. Creating, changing, testing, running, resuming and deleting a
/// replication ask for the owner's password as the pages do; stopping and reading do not. <c>inventory</c> is what a peer calls to compare
/// repositories (the peer's key needs the <c>admin:api</c> scope); the formats' own protocols carry the artifacts.
/// </summary>
[Route(ApiCatalog.RootTemplate + "/replication")]
public class ReplicationController : AdminApiController
{
    private IReplicationAdministration Replication => Service<IReplicationAdministration>();

    private ReplicationJobs Jobs => Service<ReplicationJobs>();

    private ReplicationDto Dto(ReplicationConfigView view)
    {
        var job = Jobs.Get(view.Config.Key);

        return new ReplicationDto(view, Jobs.IsRunning(view.Config.Key), job?.Refusal);
    }

    /// <summary>Lists replications.</summary>
    [HttpGet("configs")]
    [ApiOperation("List replications with their last run", "AdminReplication", "Index", Response = typeof(ReplicationDto), PagedList = true)]
    public async Task<IActionResult> List(int? page, int? pageSize)
        => RespondPage((await Replication.ListAsync(Cancellation)).Select(Dto).ToList(), page, pageSize);

    /// <summary>One replication.</summary>
    [HttpGet("configs/{key:int}")]
    [ApiOperation("Show a replication", "AdminReplication", "Config", Response = typeof(ReplicationDto))]
    public async Task<IActionResult> Get(int key)
        => await Replication.GetAsync(key, Cancellation) is { } view ? Respond(Dto(view)) : ApiProblems.NotFound($"replication {key}");

    /// <summary>Creates a replication.</summary>
    [HttpPost("configs")]
    [ApiOperation("Create a replication", "AdminReplication", "Index", Confirm = true, Response = typeof(ReplicationDto), Status = 201)]
    public async Task<IActionResult> Create([FromBody] ReplicationRequest request)
    {
        ThrowIfClosed();
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        var config = await Replication.SaveAsync(request.ToInput(0), Actor, Cancellation);

        return Respond(Dto((await Replication.GetAsync(config.Key, Cancellation))!), 201, $"{ApiCatalog.Root}/replication/configs/{config.Key}");
    }

    /// <summary>Replaces a replication's settings.</summary>
    [HttpPut("configs/{key:int}")]
    [ApiOperation("Replace a replication's settings", "AdminReplication", "Config", Confirm = true, Response = typeof(ReplicationDto))]
    public async Task<IActionResult> Update(int key, [FromBody] ReplicationRequest request)
    {
        ThrowIfClosed();
        if (await Replication.GetAsync(key, Cancellation) == null) return ApiProblems.NotFound($"replication {key}");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await Replication.SaveAsync(request.ToInput(key), Actor, Cancellation);

        return Respond(Dto((await Replication.GetAsync(key, Cancellation))!));
    }

    /// <summary>Deletes a replication.</summary>
    [HttpDelete("configs/{key:int}")]
    [ApiOperation("Delete a replication with its log (what it copied stays where it is)", "AdminReplication", "Config", Confirm = true, Status = 204)]
    public async Task<IActionResult> Delete(int key)
    {
        ThrowIfClosed();
        if (await Replication.GetAsync(key, Cancellation) == null) return ApiProblems.NotFound($"replication {key}");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await Replication.DeleteAsync(key, Actor, Cancellation);

        return Done();
    }

    /// <summary>Tests a peer connection.</summary>
    [HttpPost("test")]
    [ApiOperation("Test the connection to a peer with the given settings (nothing is saved; a stored key is used while the address is unchanged)", "AdminReplication", "Config", Confirm = true, Response = typeof(ReplicationTestResult))]
    public async Task<IActionResult> Test([FromBody] ReplicationRequest request, int? key)
    {
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        return Respond(await Replication.TestAsync(request.ToInput(key ?? 0), Cancellation));
    }

    /// <summary>Starts a run.</summary>
    [HttpPost("configs/{key:int}/run")]
    [ApiOperation("Start a run of a replication", "AdminReplication", "Config", Confirm = true, Response = typeof(ReplicationDto), Status = 202)]
    public Task<IActionResult> Run(int key) => StartAsync(key, "manual", resume: false);

    /// <summary>Starts a run that continues the last interrupted or failed one.</summary>
    [HttpPost("configs/{key:int}/resume")]
    [ApiOperation("Start a run that continues the last interrupted or failed run of a replication", "AdminReplication", "Config", Confirm = true, Response = typeof(ReplicationDto), Status = 202)]
    public Task<IActionResult> Resume(int key) => StartAsync(key, "resume", resume: true);

    private async Task<IActionResult> StartAsync(int key, string trigger, bool resume)
    {
        ThrowIfClosed();
        var view = await Replication.GetAsync(key, Cancellation);
        if (view == null) return ApiProblems.NotFound($"replication {key}");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;
        if (!view.Config.Enabled) return ApiProblems.Conflict("The replication is disabled: enable it first.");

        int? resumedFrom = null;
        if (resume)
        {
            var last = (await Replication.ListRunsAsync(key, 1, Cancellation)).FirstOrDefault();
            if (last == null || last.Status is not (ReplicationRunStatus.Failed or ReplicationRunStatus.Interrupted)) return ApiProblems.Conflict("There is no failed or interrupted run to resume.");

            resumedFrom = last.Key;
        }

        if (!Jobs.TryStart(key, trigger, Actor, resumedFrom, out var problem)) return ApiProblems.Conflict(problem ?? "The run was not started.");

        return Respond(Dto((await Replication.GetAsync(key, Cancellation))!), 202, $"{ApiCatalog.Root}/replication/configs/{key}/runs");
    }

    /// <summary>Stops the running replication.</summary>
    [HttpPost("configs/{key:int}/stop")]
    [ApiOperation("Ask the running replication to stop (it ends as interrupted and can be resumed)", "AdminReplication", "Config", Status = 202)]
    public async Task<IActionResult> Stop(int key)
    {
        if (await Replication.GetAsync(key, Cancellation) == null) return ApiProblems.NotFound($"replication {key}");

        return Jobs.Cancel(key) ? Respond(null, 202) : ApiProblems.Conflict("No run of this replication is going on this server.");
    }

    /// <summary>The log.</summary>
    [HttpGet("configs/{key:int}/runs")]
    [ApiOperation("List the runs of a replication, newest first (status, counts, the first failures and conflicts)", "AdminReplication", "Config", Response = typeof(ReplicationRun), PagedList = true)]
    public async Task<IActionResult> Runs(int key, int? page, int? pageSize)
    {
        if (await Replication.GetAsync(key, Cancellation) == null) return ApiProblems.NotFound($"replication {key}");

        return RespondPage(await Replication.ListRunsAsync(key, 500, Cancellation), page, pageSize);
    }

    /// <summary>One run.</summary>
    [HttpGet("runs/{key:int}")]
    [ApiOperation("Show one run of the replication log", "AdminReplication", "Config", Response = typeof(ReplicationRun))]
    public async Task<IActionResult> GetRun(int key)
        => await Replication.GetRunAsync(key, Cancellation) is { } run ? Respond(run) : ApiProblems.NotFound($"replication run {key}");

    /// <summary>What a repository holds.</summary>
    [HttpGet("inventory/{repository}")]
    [ApiOperation("List what a repository holds in the form replication compares (what a peer asks; summary=true: only the count)", "AdminReplication", "Config", Response = typeof(ReplicationInventory))]
    public async Task<IActionResult> Inventory(string repository, bool summary = false)
        => await Service<IReplicationInventory>().GetAsync(repository, summary, Cancellation) is { } inventory
            ? Respond(inventory)
            : ApiProblems.NotFound($"repository '{repository}'");
}
