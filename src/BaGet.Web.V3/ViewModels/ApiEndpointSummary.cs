using BaGet.Identity.Application.Screens;

namespace BaGet.Web.V3.ViewModels;

/// <summary>One operation of the REST API as the page (and the JSON twin) lists it: flat, with no reflection objects.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path template (<c>/api/v1/groups/{name}</c>).</param>
/// <param name="Summary">What the operation does.</param>
/// <param name="UiScreen">The screen that offers the same operation (<c>AdminGroups</c> / <c>Index</c>).</param>
/// <param name="Confirm">Whether the owner's password is asked (the confirmation header).</param>
public sealed record ApiEndpointSummary(string Method, string Path, string Summary, ScreenRef UiScreen, bool Confirm);
