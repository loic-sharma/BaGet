using System;
using BaGet.Identity.Application.Screens;
using BaGet.Web.V3.Models.Api;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Api.Catalog;

/// <summary>
/// Describes one operation of the administration REST API for the OpenAPI document (<see cref="ApiCatalog"/>), the
/// <c>/admin/api</c> page and the UI-coverage audit: what it does, the screen that offers the same operation
/// (<see cref="UiController"/> and <see cref="UiAction"/>, the CLAUDE.md rule "every service has a UI counterpart"), the type of its success body and whether it
/// needs the password confirmation that page asks for. Every action of an <see cref="AdminApiController"/> carries one; a test fails
/// for an action without it.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ApiOperationAttribute : Attribute
{
    /// <param name="summary">One sentence, imperative ("Create a group").</param>
    /// <param name="uiController">The view controller of the screen that offers the operation, without the suffix, such as <c>"AdminGroups"</c>.</param>
    /// <param name="uiAction">The action of that controller that shows the screen, such as <c>"Index"</c>.</param>
    public ApiOperationAttribute(string summary, string uiController, string uiAction)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        UiController = uiController ?? throw new ArgumentNullException(nameof(uiController));
        UiAction = uiAction ?? throw new ArgumentNullException(nameof(uiAction));
    }

    /// <summary>What the operation does.</summary>
    public string Summary { get; }

    /// <summary>The view controller of the screen that offers the same operation, without the <c>Controller</c> suffix.</summary>
    public string UiController { get; }

    /// <summary>The action of <see cref="UiController"/> that shows that screen.</summary>
    public string UiAction { get; }

    /// <summary>The screen that offers the same operation (controller name without the suffix, and action).</summary>
    public ScreenRef UiScreen => new(UiController, UiAction);

    /// <summary>
    /// Whether the page asks the administrator's password again for this operation, so the API asks for the
    /// <see cref="AdminApiController.ConfirmHeader"/> header (a global-key caller needs none: the key is the confirmation).
    /// </summary>
    public bool Confirm { get; set; }

    /// <summary>The type of the success body, or null when there is none.</summary>
    public Type? Response { get; set; }

    /// <summary>The success status (200 unless the operation creates, accepts or returns no content).</summary>
    public int Status { get; set; } = 200;

    /// <summary>Whether <see cref="Response"/> is a list inside a <see cref="Paged{T}"/> envelope.</summary>
    public bool PagedList { get; set; }
}
