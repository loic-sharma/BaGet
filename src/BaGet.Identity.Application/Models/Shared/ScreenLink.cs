using System.Collections.Generic;
using BaGet.Identity.Application.Screens;

namespace BaGet.Identity.Application.Models.Shared;

/// <summary>A screen as a client links to it: the screen (controller and action) and its route values; the view turns it into an address.</summary>
/// <param name="Screen">The screen (<see cref="ScreenCatalog.Package"/>).</param>
/// <param name="RouteValues">Route values; a null value (<c>repo</c> of the default repository) is kept.</param>
public sealed record ScreenLink(ScreenRef Screen, IReadOnlyDictionary<string, object?> RouteValues);
