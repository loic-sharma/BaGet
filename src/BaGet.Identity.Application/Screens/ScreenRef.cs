namespace BaGet.Identity.Application.Screens;

/// <summary>
/// A screen of the web UI: the MVC controller (without the <c>Controller</c> suffix, as <c>asp-controller</c> writes it) and the action that
/// answers a plain <c>GET</c> for it (<c>AdminUsers</c> / <c>Index</c>). The Application projects have no MVC reference, so the UI project
/// turns it into a link. <see cref="ScreenCatalog"/> lists every screen.
/// </summary>
/// <param name="Controller">The controller name without the <c>Controller</c> suffix (<c>AdminUsers</c>).</param>
/// <param name="Action">The action that shows the screen (<c>Index</c>).</param>
public sealed record ScreenRef(string Controller, string Action)
{
    /// <summary><c>Controller/Action</c>, the form the contracts and the coverage table write a screen in.</summary>
    public override string ToString() => Controller + "/" + Action;
}
