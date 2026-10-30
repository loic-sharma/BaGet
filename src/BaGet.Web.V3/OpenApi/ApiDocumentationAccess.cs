using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Web.V3.OpenApi;

/// <summary>
/// Who may read the documents and Scalar (decision D7): a signed-in server administrator, or an administrator's API key with the
/// <c>admin:api</c> scope that is not limited to repositories, or the server's global <c>ApiKey</c> (only when one is configured: an
/// open server with no key makes nobody an administrator). Anyone else gets 404, so the endpoints are not even visible.
/// </summary>
public static class ApiDocumentationAccess
{
    /// <summary>True when the request's caller may read the API documentation.</summary>
    public static async Task<bool> IsAllowedAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var resolver = services.GetRequiredService<RequestPrincipalResolver>();

        // A key (Basic password, Bearer, X-NuGet-ApiKey) is judged like the /api/v1 check; it never falls back to the cookie when it
        // authenticates but is not enough.
        var key = await resolver.ResolveForWriteAsync(http, cancellationToken).ConfigureAwait(false);
        if (key != null)
        {
            if (key.IsSuperuser) return !string.IsNullOrEmpty(services.GetRequiredService<IOptionsSnapshot<BaGetOptions>>().Value.ApiKey);

            return key.IsAdministrator && key.HasScope(ApiKeyScopes.Admin) && key.RepositoryKeys == null;
        }

        // The browser session of a server administrator (a plain GET: nothing is changed, so the cookie is safe here).
        var session = await resolver.ResolveAsync(http, cancellationToken).ConfigureAwait(false);

        return session is { IsSuperuser: false, IsAdministrator: true };
    }
}
