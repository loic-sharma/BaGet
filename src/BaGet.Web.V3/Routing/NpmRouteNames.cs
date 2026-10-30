using BaGet.Core;
using Microsoft.AspNetCore.Routing;

namespace BaGet.Web.V3.Routing;

/// <summary>Reads the package name an npm route carries.</summary>
public static class NpmRouteNames
{
    /// <summary>
    /// The raw package name of an npm request: <c>name</c> (which may still hold the encoded <c>@scope%2fname</c>), or
    /// <c>@{scope}/{pkg}</c> of a route that carries the scope as its own segment; null for a route without a package.
    /// Validate it with <see cref="NpmNames.TryParse"/>.
    /// </summary>
    public static string? FromRouteValues(RouteValueDictionary values)
    {
        if (values.TryGetValue("name", out var name) && name is string n) return n;
        if (values.TryGetValue("scope", out var scope) && scope is string s && values.TryGetValue("pkg", out var pkg) && pkg is string p)
        {
            return "@" + s + "/" + p;
        }

        return null;
    }
}
