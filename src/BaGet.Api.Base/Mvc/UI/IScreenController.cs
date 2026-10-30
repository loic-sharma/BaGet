namespace BaGet.Frontend.Mvc.UI;

/// <summary>
/// Marks a controller as a view controller of the web UI (a screen). The API assemblies cannot see the front-end base class, so
/// <see cref="ScreenEndpoints"/> tells a screen action from every other endpoint by this marker, which the front-end base
/// (<c>UiControllerBase</c>) implements; the JSON twins under <c>/api/ui</c> never do.
/// </summary>
public interface IScreenController
{
}
