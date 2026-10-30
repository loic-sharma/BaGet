using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Settings;
using BaGet.Identity.Application.UseCases.Admin;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The server settings screen (<c>/admin/settings</c>): edits every registered setting, one form per section, the per-source log
/// levels, the database copy progress (<c>?handler=CopyStatus</c>) and the telemetry connection test. It replaces the Razor Page
/// <c>/Admin/Settings</c> (same URL and handlers); the work is done by the JSON twin
/// (<see cref="AdminSettingsApiController"/>) and this controller only renders its answers. Every change asks the administrator's
/// password again and is refused with 503 while the read-only gate is closed.
/// </summary>
public sealed class AdminSettingsController : AdminControllerBase
{
    /// <summary>Shows the settings. <paramref name="section"/> is the id of the section to show.</summary>
    [Route("admin/settings"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(string? section = null)
        => RenderFlashed(await Api<AdminSettingsApiController, AdminSettingsViewModel>(api => api.Get(section)), "Index");

    /// <summary>The progress of the database copy as JSON, polled by the page.</summary>
    [Route("admin/settings"), HttpGet, PageHandler("CopyStatus")]
    public async Task<IActionResult> CopyStatus()
    {
        var response = await Api<AdminSettingsApiController, DatabaseCopyStatusView>(api => Task.FromResult(api.GetCopyStatus()));

        // The page's script reads the twin's model as it is: plain JSON, not a view.
        return response.Raw ?? (response.Screen.Status == ScreenStatus.Ok ? new JsonResult(response.Screen.Model) : NotFound());
    }

    /// <summary>Saves the settings of one section after the administrator confirms.</summary>
    [Route("admin/settings"), HttpPost, PageHandler]
    public async Task<IActionResult> Save(string? section, string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminSettingsApiController, AdminSettingsViewModel>(api => api.Save(Submission(section, password), cancellationToken)), "Index");

    /// <summary>Adds or changes a named OpenID Connect provider (the "Sign-in providers" panel).</summary>
    [Route("admin/settings"), HttpPost, PageHandler("SaveProvider")]
    public async Task<IActionResult> SaveProvider(SignInProviderForm form, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminSettingsApiController, AdminSettingsViewModel>(api => api.SaveProvider(form, cancellationToken)), "Index");

    /// <summary>Removes a named OpenID Connect provider saved on this screen.</summary>
    [Route("admin/settings"), HttpPost, PageHandler("RemoveProvider")]
    public async Task<IActionResult> RemoveProvider(string? id, string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminSettingsApiController, AdminSettingsViewModel>(api => api.RemoveProvider(new RemoveSignInProviderForm(id, password), cancellationToken)), "Index");

    /// <summary>Sets the lowest level logged for one source.</summary>
    [Route("admin/settings"), HttpPost, PageHandler("SetLogLevel")]
    public async Task<IActionResult> SetLogLevel(string? source, string? level, string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminSettingsApiController, AdminSettingsViewModel>(api => api.SetLogLevel(new LogLevelForm(source, level, password), cancellationToken)), "Index");

    /// <summary>Removes a per-source level saved on this page.</summary>
    [Route("admin/settings"), HttpPost, PageHandler("RemoveLogLevel")]
    public async Task<IActionResult> RemoveLogLevel(string? source, string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminSettingsApiController, AdminSettingsViewModel>(api => api.RemoveLogLevel(new LogLevelForm(source, null, password), cancellationToken)), "Index");

    /// <summary>Sends one test span to the posted, unsaved telemetry endpoint and shows the outcome.</summary>
    [Route("admin/settings"), HttpPost, PageHandler("TestTelemetry")]
    public async Task<IActionResult> TestTelemetry(string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminSettingsApiController, AdminSettingsViewModel>(api => api.TestTelemetry(Submission(null, password), cancellationToken)), "Index");

    // The posted form as the use case takes it: every field, as the page's form collection holds it.
    private SettingsSubmission Submission(string? section, string? password)
        => new(
            section,
            password,
            Request.HasFormContentType
                ? Request.Form.ToDictionary(field => field.Key, field => field.Value.Select(value => value ?? string.Empty).ToArray())
                : new Dictionary<string, string[]>());
}
