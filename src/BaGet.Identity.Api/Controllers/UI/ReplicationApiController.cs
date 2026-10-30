using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Policies;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.UseCases.Policies;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the replication screens (<c>/api/ui/admin/replication</c>): the replications and one replication (save, test the connection, run,
/// resume, stop, delete, its log). <c>AdminReplicationController</c> calls it in process. Administrators only; everything but stopping asks
/// the administrator's password in the body; every write is refused with 503 while the server is read-only. The peer API key is write only: it can
/// be sent in <c>form.apiKey</c> and is never part of an answer.
/// </summary>
[Route("api/ui/admin/replication")]
public sealed class ReplicationApiController : AccessApiControllerBase
{
    private ReplicationUseCase Replication => UseCase<ReplicationUseCase>();

    private string ReturnPage => "~" + Request.Path + Request.QueryString;

    // ---------------------------------------------------------------- replications

    /// <summary>Lists the replications and the form that creates one.</summary>
    [HttpGet(""), UiApi("AdminReplication", "Index")]
    public async Task<ActionResult<ScreenResult<ReplicationViewModel>>> GetReplications(CancellationToken cancellationToken = default)
        => Respond(await Replication.GetReplicationsAsync(User, ReturnPage, cancellationToken));

    /// <summary>Creates a replication and opens it.</summary>
    [HttpPost(""), UiApi("AdminReplication", "Index")]
    public async Task<ActionResult<ScreenResult<ReplicationViewModel>>> Create([FromBody] ReplicationBody? body, CancellationToken cancellationToken = default)
        => Respond(await Replication.CreateAsync(User, body, ReturnPage, cancellationToken));

    // ---------------------------------------------------------------- one replication

    /// <summary>Shows one replication with its log.</summary>
    [HttpGet("{key:int}"), UiApi("AdminReplication", "Config")]
    public async Task<ActionResult<ScreenResult<ReplicationConfigViewModel>>> GetConfig(int key, CancellationToken cancellationToken = default)
        => Respond(await Replication.GetConfigAsync(User, key, ReturnPage, cancellationToken));

    /// <summary>Saves the settings.</summary>
    [HttpPost("{key:int}/save"), UiApi("AdminReplication", "Config", Handler = "Save")]
    public async Task<ActionResult<ScreenResult<ReplicationConfigViewModel>>> Save(int key, [FromBody] ReplicationBody? body, CancellationToken cancellationToken = default)
        => Respond(await Replication.SaveAsync(User, key, body, cancellationToken));

    /// <summary>Tests the connection with the typed settings; saves nothing.</summary>
    [HttpPost("{key:int}/test"), UiApi("AdminReplication", "Config", Handler = "Test")]
    public async Task<ActionResult<ScreenResult<ReplicationConfigViewModel>>> Test(int key, [FromBody] ReplicationBody? body, CancellationToken cancellationToken = default)
        => Respond(await Replication.TestAsync(User, key, body, ReturnPage, cancellationToken));

    /// <summary>Starts a run.</summary>
    [HttpPost("{key:int}/run"), UiApi("AdminReplication", "Config", Handler = "Run")]
    public async Task<ActionResult<ScreenResult<ReplicationConfigViewModel>>> Run(int key, [FromBody] PasswordForm? body, CancellationToken cancellationToken = default)
        => Respond(await Replication.RunAsync(User, key, body?.Password, cancellationToken));

    /// <summary>Starts a run that continues the last interrupted or failed one.</summary>
    [HttpPost("{key:int}/resume"), UiApi("AdminReplication", "Config", Handler = "Resume")]
    public async Task<ActionResult<ScreenResult<ReplicationConfigViewModel>>> Resume(int key, [FromBody] PasswordForm? body, CancellationToken cancellationToken = default)
        => Respond(await Replication.ResumeAsync(User, key, body?.Password, cancellationToken));

    /// <summary>Asks the running replication to stop; it ends as interrupted and can be resumed.</summary>
    [HttpPost("{key:int}/stop"), UiApi("AdminReplication", "Config", Handler = "Stop")]
    public async Task<ActionResult<ScreenResult<ReplicationConfigViewModel>>> Stop(int key, CancellationToken cancellationToken = default)
        => Respond(await Replication.StopAsync(key, cancellationToken));

    /// <summary>Deletes the configuration with its log.</summary>
    [HttpPost("{key:int}/delete"), UiApi("AdminReplication", "Config", Handler = "Delete")]
    public async Task<ActionResult<ScreenResult<ReplicationConfigViewModel>>> Delete(int key, [FromBody] PasswordForm? body, CancellationToken cancellationToken = default)
        => Respond(await Replication.DeleteAsync(User, key, body?.Password, cancellationToken));
}
