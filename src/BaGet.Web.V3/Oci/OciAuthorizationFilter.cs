using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using BaGet.Web.V3.Filters;
using BaGet.Web.V3.Routing;

namespace BaGet.Web.V3.Oci;

/// <summary>
/// The access check of every <c>/v2/{repo}/{image…}/…</c> request, in one place (the routes are
/// <see cref="RepositoryRouteMetadata.ResolvesRepositoryItself"/>, so neither <see cref="RepositoryReadFilter"/> nor the repository
/// middleware has looked at them). It parses the path (<see cref="OciNames.Parse"/>), maps method and path to the action needed
/// (<see cref="OciHttp.Required"/>: pull = Read, push = Deploy, delete = Delete; a request that needs nothing defined is refused),
/// finds the repository (Docker: local, remote or virtual), asks <see cref="OciCallerResolver"/> whether the caller holds the action
/// on the image, and only then selects the repository for the rest of the request.
/// <para>A <b>remote</b> repository takes no push or delete: a caller who may pull it is told so (<c>403 DENIED</c>) rather than
/// challenged again. A <b>virtual</b> one is pulled with the caller's rights in each member (the caller's registry token carries a
/// pull grant per member it was issued for) and written through its deploy repository: a push or delete is authorized, and runs, in
/// that repository (<see cref="OciRequest.Target"/>); a virtual repository without one answers <c>403 DENIED</c>.</para>
/// <para>
/// A caller who is anonymous, or presents a registry token that does not cover the request, gets <c>401</c> with the token
/// challenge, <b>whether the repository exists or not</b>, so a client cannot tell a private repository from a missing one
/// (Docker then fetches a token and tries again). A caller with an API key and no Read gets <c>404</c>; one with Read but not
/// the action asked for gets <c>403</c>.
/// </para>
/// </summary>
public class OciAuthorizationFilter : IAsyncResourceFilter
{
    private readonly OciCallerResolver _callers;
    private readonly IVirtualRepositories _virtuals;
    private readonly IOciRequestAccess _memberAccess;
    private readonly IRepositoryService _repositories;
    private readonly IRepositoryContext _repositoryContext;
    private readonly IUrlGenerator _urls;
    private readonly IOptionsSnapshot<OciOptions> _options;
    private readonly IOptionsSnapshot<BaGetOptions> _baGetOptions;

    public OciAuthorizationFilter(
        OciCallerResolver callers,
        IVirtualRepositories virtuals,
        IOciRequestAccess memberAccess,
        IRepositoryService repositories,
        IRepositoryContext repositoryContext,
        IUrlGenerator urls,
        IOptionsSnapshot<OciOptions> options,
        IOptionsSnapshot<BaGetOptions> baGetOptions)
    {
        _callers = callers ?? throw new ArgumentNullException(nameof(callers));
        _virtuals = virtuals ?? throw new ArgumentNullException(nameof(virtuals));
        _memberAccess = memberAccess ?? throw new ArgumentNullException(nameof(memberAccess));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _repositoryContext = repositoryContext ?? throw new ArgumentNullException(nameof(repositoryContext));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _baGetOptions = baGetOptions ?? throw new ArgumentNullException(nameof(baGetOptions));
    }

    /// <inheritdoc/>
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers[OciHttp.ApiVersionHeader] = OciHttp.ApiVersion;

        // The version check and the token service answer for themselves: they are not about one repository.
        var action = (context.ActionDescriptor as ControllerActionDescriptor)?.ActionName;
        if (action is "Base" or "Token")
        {
            await next();
            return;
        }

        var rest = context.RouteData.Values.TryGetValue("path", out var path) ? path as string : null;
        var name = context.RouteData.Values.TryGetValue(Routes.RepositoryRouteKey, out var repo) ? repo as string : null;
        var parsed = OciNames.Parse(rest);
        if (parsed == null)
        {
            var looksLikeRegistryPath = rest != null && (rest.Contains("/blobs/") || rest.Contains("/manifests/") || rest.Contains("/tags/list") || rest.Contains("/referrers/"));
            context.Result = looksLikeRegistryPath
                ? OciHttp.Error(StatusCodes.Status400BadRequest, "NAME_INVALID", "The repository name, tag or digest is not valid.")
                : OciHttp.Error(StatusCodes.Status404NotFound, "UNSUPPORTED", "This is not a registry endpoint.");
            return;
        }

        var required = OciHttp.Required(http.Request.Method, parsed.Kind);
        if (required == null)
        {
            context.Result = OciHttp.Error(StatusCodes.Status405MethodNotAllowed, "UNSUPPORTED", "The method is not supported for this endpoint.");
            return;
        }

        var cancellationToken = http.RequestAborted;
        var found = string.IsNullOrEmpty(name) ? null : await _repositories.FindAsync(name.ToLowerInvariant(), cancellationToken);
        var repository = found is { Format: RepositoryFormat.Docker } ? found : null;
        var caller = await _callers.ResolveAsync(http, cancellationToken);

        // Where the request is authorized and runs: the repository itself, except a write to a virtual one, which is its deploy repository's.
        var target = repository;
        if (repository is { Kind: RepositoryKind.Virtual } && required != OciActions.Pull)
        {
            target = await _virtuals.DeployTargetAsync(repository, cancellationToken);
        }

        var allowed = repository != null && await IsAllowedAsync(caller, repository, target, parsed.Image, required.Value, cancellationToken);

        if (!allowed)
        {
            // A repository that takes no such change says so to a caller who may use it, instead of sending a client round the token flow.
            if (repository != null && required != OciActions.Pull && (repository.Kind == RepositoryKind.Remote || target == null)
                && await _callers.AllowsAsync(caller, repository, parsed.Image, OciActions.Pull, cancellationToken))
            {
                context.Result = OciHttp.Error(
                    StatusCodes.Status403Forbidden,
                    "DENIED",
                    repository.Kind == RepositoryKind.Remote
                        ? "This repository caches an upstream registry and takes no pushes or deletes."
                        : "This virtual repository has no deploy repository, so it takes no pushes or deletes.");
                return;
            }

            if (caller.Token != null || caller.IsAnonymous || caller.TokenRefused)
            {
                var scope = OciScopes.Format(new OciGrant((name ?? string.Empty).ToLowerInvariant(), parsed.Image, OciHttp.ChallengeActions(required.Value)));
                var error = caller.Token != null ? "insufficient_scope" : caller.TokenRefused ? "invalid_token" : null;
                http.Response.Headers.WWWAuthenticate = new StringValues(OciHttp.Challenge(_urls.GetOciRegistryUrl() + "token", _options.Value.Service, scope, error));
                context.Result = OciHttp.Error(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "authentication required");
                return;
            }

            // An API key that may read the image is told what it lacks; anything else is "not there", as for a missing repository.
            var mayRead = repository != null && await _callers.AllowsAsync(caller, repository, parsed.Image, OciActions.Pull, cancellationToken);
            context.Result = mayRead
                ? OciHttp.Error(StatusCodes.Status403Forbidden, "DENIED", "requested access to the resource is denied")
                : OciHttp.Error(StatusCodes.Status404NotFound, "NAME_UNKNOWN", $"The repository '{parsed.Image}' does not exist.");
            return;
        }

        // A virtual repository is read through its members: how the caller's right to each is decided (a token's grants, a key's rights).
        _memberAccess.Use((member, image, ct) => _callers.AllowsAsync(caller, member, image, OciActions.Pull, ct));

        if (required != OciActions.Pull && _baGetOptions.Value.IsReadOnlyMode)
        {
            context.Result = OciHttp.Error(StatusCodes.Status403Forbidden, "DENIED", "This server is read only.");
            return;
        }

        _repositoryContext.Use(target!);
        http.Items[OciRequest.ItemKey] = new OciRequest(parsed, repository!, caller, target!);

        await next();
    }

    // Whether the caller holds the action. Pulls, and everything in a local repository, are judged in the repository itself; a remote one
    // allows only pulls; a write to a virtual one is judged where it lands. A registry token names the repository the client used (a
    // virtual one included), a key is judged in the repository that does the work.
    private async Task<bool> IsAllowedAsync(OciCaller caller, RepositoryInfo repository, RepositoryInfo? target, string image, OciActions action, CancellationToken cancellationToken)
    {
        if (action == OciActions.Pull || repository.Kind == RepositoryKind.Local)
        {
            return await _callers.AllowsAsync(caller, repository, image, action, cancellationToken);
        }

        if (repository.Kind != RepositoryKind.Virtual || target == null) return false;

        return await _callers.AllowsAsync(caller, repository, target, image, action, cancellationToken);
    }
}
