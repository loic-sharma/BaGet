using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Web.V3.Routing;

namespace BaGet.Web.V3.Filters;

/// <summary>
/// The read check of every protocol route (service index, search, autocomplete, registration, content, V2, catalog,
/// symbol downloads, vulnerabilities, signatures; whatever the HTTP method, since an action not restricted to <c>GET</c>
/// also answers <c>POST</c>, <c>PUT</c> or <c>OPTIONS</c>), under <c>/</c> and <c>/r/{repo}/</c>:
/// the caller needs <see cref="AccessAction.Read"/> on the request's repository, and on the package for routes that name
/// one. An anonymous caller is answered <c>401</c> with a Basic challenge, which makes NuGet clients send credentials;
/// an authenticated one without access gets <c>404</c>, the same answer as an unknown repository or package, so nothing
/// says that a private one exists. Routes that only accept writes (<c>PUT</c>, <c>POST</c>, <c>DELETE</c>; <see cref="RepositoryRouteMetadata.WritesOnly"/>)
/// and the validation status (<see cref="RepositoryRouteMetadata.AuthorizesItself"/>) authenticate with an API key and are authorized by <see cref="IPackageAuthorizationService"/>. Runs before model binding, as a resource filter, and is
/// registered with MVC itself, so a host cannot forget it.
/// </summary>
public class RepositoryReadFilter : IAsyncResourceFilter
{
    /// <inheritdoc/>
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var route = http.GetEndpoint()?.Metadata.GetMetadata<RepositoryRouteMetadata>();
        if (route == null
            || route.AuthorizesItself
            || (route.WritesOnly && !(HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))))
        {
            await next();
            return;
        }

        var services = http.RequestServices;
        var evaluator = services.GetRequiredService<IAccessEvaluator>();
        var repository = services.GetRequiredService<IRepositoryContext>().Current;
        var packageId = context.RouteData.Values.TryGetValue("id", out var id) ? id as string : null;
        if (packageId == null && http.Request.Query.TryGetValue("id", out var queryId) && !string.IsNullOrWhiteSpace(queryId.FirstOrDefault()))
        {
            // V2 FindPackagesById()/Packages() and v3/autocomplete name the package in the query string. MVC binds the FIRST value of a
            // repeated key, so that is the one judged (ToString() would join them and match no exclusion).
            packageId = queryId.FirstOrDefault();
        }

        packageId = UnquoteLiteral(packageId);
        if (string.IsNullOrEmpty(packageId)) packageId = null;
        Func<Task<string?>>? resolveName = null;
        if (route.Format == RepositoryFormat.Npm)
        {
            // npm routes name the package in "name" (possibly @scope%2fname) or "scope" and "pkg"; the access patterns see the
            // decoded name, so a route cannot reach a package that its pattern would hide by writing the name differently.
            var raw = NpmRouteNames.FromRouteValues(context.RouteData.Values);
            if (raw != null)
            {
                if (!NpmNames.TryParseLookup(raw, out var normalizedName, out _))
                {
                    context.Result = new NotFoundResult();
                    return;
                }

                packageId = normalizedName;
            }
        }

        if (route.Format == RepositoryFormat.Maven)
        {
            // A Maven route names the artifact through its path: the access patterns see group:artifact, which the repository
            // works out from what is stored (metadata can name an artifact or one of its versions).
            var requested = MavenNames.ParsePath(context.RouteData.Values.TryGetValue("path", out var raw) ? raw as string : null);
            if (requested == null)
            {
                context.Result = new NotFoundResult();
                return;
            }

            // Looked up only once the repository itself may be read: a remote repository asks its upstream to tell a version's metadata
            // from an artifact's, which nobody who may not read the repository should be able to make it do.
            var maven = services.GetRequiredService<IMavenRepository>();
            resolveName = async () => (await maven.ResolveAsync(requested, http.RequestAborted))?.Name;
        }

        async Task<bool> MayReadAsync(BaGetPrincipal? caller)
        {
            if (!await evaluator.IsAllowedAsync(caller, repository, packageId: null, AccessAction.Read, http.RequestAborted)) return false;

            if (resolveName != null)
            {
                packageId = await resolveName();
                resolveName = null;
            }

            return packageId == null
                || await evaluator.IsAllowedAsync(caller, repository, packageId, AccessAction.Read, http.RequestAborted);
        }

        // Open to everyone is the common case (and the only one before the access model): answered without looking at
        // the credentials, so a public feed costs no API key lookup even for a client that sends them.
        if (await MayReadAsync(null))
        {
            await next();
            return;
        }

        var principal = await services.GetRequiredService<RequestPrincipalResolver>().ResolveAsync(http, http.RequestAborted);
        if (principal != null && await MayReadAsync(principal))
        {
            await next();
            return;
        }

        if (principal == null)
        {
            // npm answers a 401 with E401 and never retries; its clients send Bearer, NuGet's send Basic.
            http.Response.Headers.WWWAuthenticate = route.Format == RepositoryFormat.Npm
                ? new Microsoft.Extensions.Primitives.StringValues(new[] { BasicCredentials.BearerChallenge, BasicCredentials.Challenge })
                : BasicCredentials.Challenge;
            context.Result = new UnauthorizedResult();
        }
        else
        {
            context.Result = new NotFoundResult();
        }
    }

    /// <summary>
    /// The V2 feed names a package as an OData string literal (<c>Id='X'</c>); the access patterns must see the bare id,
    /// whatever form the client wrote.
    /// </summary>
    internal static string? UnquoteLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var trimmed = value!.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\''
            ? trimmed.Substring(1, trimmed.Length - 2).Replace("''", "'")
            : trimmed;
    }
}
