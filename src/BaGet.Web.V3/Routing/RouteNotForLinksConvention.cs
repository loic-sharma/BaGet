using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace BaGet.Web.V3.Routing;

/// <summary>
/// Applies <see cref="RouteNotForLinksAttribute"/>: sets <c>SuppressLinkGeneration</c> on the listed selectors of an action. Must be
/// registered before <see cref="RepositoryActionRouteConvention"/> so the repository twins inherit the setting.
/// </summary>
public sealed class RouteNotForLinksConvention : IApplicationModelConvention
{
    /// <inheritdoc/>
    public void Apply(ApplicationModel application)
    {
        if (application == null) throw new ArgumentNullException(nameof(application));

        foreach (var action in application.Controllers.SelectMany(c => c.Actions))
        {
            var attribute = action.Attributes.OfType<RouteNotForLinksAttribute>().FirstOrDefault();
            if (attribute == null) continue;

            foreach (var selector in action.Selectors)
            {
                var route = selector.AttributeRouteModel;
                if (route != null && attribute.Templates.Contains(route.Template ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    route.SuppressLinkGeneration = true;
                }
            }
        }
    }
}
