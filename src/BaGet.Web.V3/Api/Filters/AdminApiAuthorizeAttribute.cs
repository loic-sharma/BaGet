using System;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Security;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using BaGet.Web.V3.Api;

namespace BaGet.Web.V3.Api.Filters;

/// <summary>
/// Lets only administrators in. The caller is the principal of an API key (<c>Authorization: Basic</c> password, <c>Bearer</c>,
/// <c>X-NuGet-ApiKey</c>), resolved by <see cref="RequestPrincipalResolver.ResolveForWriteAsync"/> for every method, reads
/// included: the browser session is never accepted, so a web page cannot make the administrator's browser call the API. Accepted:
/// the global <c>ApiKey</c> of the server (when one is configured: an open server with no key never makes anyone an
/// administrator), or the key of an administrator that carries <see cref="ApiKeyScopes.Admin"/> and is not limited to repositories.
/// No credentials: 401 with a <c>Bearer</c> challenge. Valid ones without the right: 403.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class AdminApiAuthorizeAttribute : Attribute, IAsyncAuthorizationFilter
{
    internal const string CallerItem = "BaGet.AdminApi.Caller";

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        var principal = await services.GetRequiredService<RequestPrincipalResolver>().ResolveForWriteAsync(http, http.RequestAborted);

        if (principal == null)
        {
            http.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer realm=\"BaGet administration API\"";
            context.Result = ApiProblems.Create(401, "unauthorized", "Unauthorized", "Send an administrator's API key with the admin:api scope (Authorization: Bearer <key>), or the server's global API key.");

            return;
        }

        if (principal.IsSuperuser)
        {
            // An open server (no global key) authenticates everybody as the superuser; that must never open the administration.
            if (string.IsNullOrEmpty(services.GetRequiredService<IOptionsSnapshot<BaGetOptions>>().Value.ApiKey))
            {
                context.Result = ApiProblems.Create(403, "forbidden", "Forbidden", "The server has no global ApiKey, so only an administrator's key with the admin:api scope is accepted.");

                return;
            }
        }
        else if (!principal.IsAdministrator || !principal.HasScope(ApiKeyScopes.Admin))
        {
            context.Result = ApiProblems.Create(403, "forbidden", "Forbidden", "The key must belong to an administrator and carry the admin:api scope.");

            return;
        }
        else if (principal.RepositoryKeys != null)
        {
            context.Result = ApiProblems.Create(403, "forbidden", "Forbidden", "A key limited to repositories cannot administer the server.");

            return;
        }

        http.Items[CallerItem] = principal;
    }
}
