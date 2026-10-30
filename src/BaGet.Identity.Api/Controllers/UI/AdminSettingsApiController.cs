using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Settings;
using BaGet.Identity.Application.UseCases.Admin;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the server settings screen (<c>/api/ui/admin/settings</c>): every registered setting (a sensitive one never carries its value), a save per
/// section, the per-source log levels, the database copy progress and the telemetry connection test. <c>AdminSettingsController</c> calls it in process.
/// Every change asks the administrator's password in the body and is refused with 503 while the server is read-only (the connection test writes nothing, so it
/// is not). A save that is refused answers 400 with the screen's model and the reasons.
/// </summary>
[Route("api/ui/admin/settings")]
public sealed class AdminSettingsApiController : AdminApiControllerBase
{
    private AdminSettingsUseCase Settings => UseCase<AdminSettingsUseCase>();

    /// <summary>Shows the settings; <paramref name="section"/> is the id of the section to show.</summary>
    [HttpGet(""), UiApi("AdminSettings", "Index")]
    public async Task<ActionResult<ScreenResult<AdminSettingsViewModel>>> Get(string? section = null)
        => Respond(await Settings.GetAsync(User, section));

    /// <summary>The progress of the database copy.</summary>
    [HttpGet("copy-status"), UiApi("AdminSettings", "Index", Handler = "CopyStatus")]
    public ActionResult<ScreenResult<DatabaseCopyStatusView>> GetCopyStatus()
        => Respond(Settings.GetCopyStatus());

    /// <summary>
    /// Saves the settings of one section: the body names the section, the password and the fields (<c>v:&lt;key&gt;</c> values, <c>reset:&lt;key&gt;</c>,
    /// <c>clear:&lt;key&gt;</c>, <c>copyData</c>), each an array of strings.
    /// </summary>
    [HttpPost(""), UiApi("AdminSettings", "Index")]
    public async Task<ActionResult<ScreenResult<AdminSettingsViewModel>>> Save([FromBody] SettingsSubmission? submission, CancellationToken cancellationToken = default)
        => Respond(await Settings.SaveAsync(User, Normalize(submission), cancellationToken));

    /// <summary>Adds or changes a named OpenID Connect provider (password in the body; takes effect after a restart; a blank secret keeps the saved one).</summary>
    [HttpPost("save-provider"), UiApi("AdminSettings", "Index", Handler = "SaveProvider")]
    public async Task<ActionResult<ScreenResult<AdminSettingsViewModel>>> SaveProvider([FromBody] SignInProviderForm? form, CancellationToken cancellationToken = default)
        => Respond(await Settings.SaveSignInProviderAsync(User, form, cancellationToken));

    /// <summary>Removes a named OpenID Connect provider saved on this screen (takes effect after a restart).</summary>
    [HttpPost("remove-provider"), UiApi("AdminSettings", "Index", Handler = "RemoveProvider")]
    public async Task<ActionResult<ScreenResult<AdminSettingsViewModel>>> RemoveProvider([FromBody] RemoveSignInProviderForm? form, CancellationToken cancellationToken = default)
        => Respond(await Settings.RemoveSignInProviderAsync(User, form, cancellationToken));

    /// <summary>Sets the lowest level logged for one source.</summary>
    [HttpPost("set-log-level"), UiApi("AdminSettings", "Index", Handler = "SetLogLevel")]
    public async Task<ActionResult<ScreenResult<AdminSettingsViewModel>>> SetLogLevel([FromBody] LogLevelForm? form, CancellationToken cancellationToken = default)
        => Respond(await Settings.SetLogLevelAsync(User, form?.Source, form?.Level, form?.Password, cancellationToken));

    /// <summary>Removes a per-source level saved on this screen.</summary>
    [HttpPost("remove-log-level"), UiApi("AdminSettings", "Index", Handler = "RemoveLogLevel")]
    public async Task<ActionResult<ScreenResult<AdminSettingsViewModel>>> RemoveLogLevel([FromBody] LogLevelForm? form, CancellationToken cancellationToken = default)
        => Respond(await Settings.RemoveLogLevelAsync(User, form?.Source, form?.Password, cancellationToken));

    /// <summary>Sends one test span to the posted, unsaved telemetry endpoint (<c>v:Telemetry:Otlp:*</c> fields) and answers the outcome; nothing is saved.</summary>
    [HttpPost("test-telemetry"), UiApi("AdminSettings", "Index", Handler = "TestTelemetry")]
    public async Task<ActionResult<ScreenResult<AdminSettingsViewModel>>> TestTelemetry([FromBody] SettingsSubmission? submission, CancellationToken cancellationToken = default)
        => Respond(await Settings.TestTelemetryAsync(User, Normalize(submission), cancellationToken));

    private static SettingsSubmission Normalize(SettingsSubmission? submission)
        => new(submission?.Section, submission?.Password, submission?.Fields ?? new Dictionary<string, string[]>());
}
