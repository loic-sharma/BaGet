using BaGet.Core;
using BaGet.Web.V3.Filters;
using BaGet.Web.V3.Middleware;

namespace BaGet.Web.V3.Routing;

/// <summary>
/// Marks the endpoints <see cref="BaGetEndpointBuilder"/> registers through its single <c>MapRoute</c>: the protocol
/// routes of a repository, which <see cref="RepositoryReadFilter"/> protects. A controller a host maps itself carries no
/// marker and is left alone.
/// </summary>
public sealed class RepositoryRouteMetadata
{
    /// <summary>A read route: checked for every HTTP method, since its action may answer any of them.</summary>
    public static readonly RepositoryRouteMetadata Instance = new(writesOnly: false, authorizesItself: false, RepositoryFormat.NuGet);

    private static readonly RepositoryRouteMetadata WriteOnly = new(writesOnly: true, authorizesItself: false, RepositoryFormat.NuGet);
    private static readonly RepositoryRouteMetadata SelfAuthorized = new(writesOnly: false, authorizesItself: true, RepositoryFormat.NuGet);

    private RepositoryRouteMetadata(bool writesOnly, bool authorizesItself, RepositoryFormat format, bool writesContent = true, bool resolvesRepositoryItself = false)
    {
        ResolvesRepositoryItself = resolvesRepositoryItself;
        WritesContent = writesContent;
        WritesOnly = writesOnly;
        AuthorizesItself = authorizesItself;
        Format = format;
    }

    /// <summary>
    /// The format whose protocol the route speaks (<see cref="IFormatModule"/>). <see cref="RepositoryResolutionMiddleware"/>
    /// answers 404 when the request's repository has another format, so an npm route never reaches a NuGet repository nor the
    /// reverse. Endpoints without this marker (the Razor Pages) are NuGet's.
    /// </summary>
    public RepositoryFormat Format { get; }

    /// <summary>
    /// True for a route constrained to write methods (publish, symbol upload, delete, relist, deprecation and
    /// vulnerability PUT/DELETE): its callers authenticate with an API key and are authorized by
    /// <see cref="IPackageAuthorizationService"/>, so the read check skips it for those methods (a <c>GET</c> or
    /// <c>HEAD</c> to it is still checked).
    /// </summary>
    public bool WritesOnly { get; }

    /// <summary>
    /// True for a <c>GET</c> route that exists for the pusher and authorizes it itself with an API key per repository
    /// and package: the validation status of a submission (<c>ValidationStatusController</c>, Push or Manage on the
    /// package). A pusher holding Deploy without Read (a drop box) must see what became of its push, and the route
    /// reveals nothing to anyone who may not push that package, so the read check skips it on purpose.
    /// </summary>
    public bool AuthorizesItself { get; }

    /// <summary>
    /// False for a route that does not change the repository's content though it is a write method: the credential calls
    /// (npm's login and logout). <see cref="RepositoryResolutionMiddleware"/> leaves such a request on the repository it
    /// addressed instead of moving it to a virtual repository's deploy repository, so a token issued by a login on a virtual
    /// repository is a token of that virtual repository.
    /// </summary>
    public bool WritesContent { get; }

    /// <summary>
    /// True for a route that finds its repository itself and answers an unknown or forbidden one in its own protocol (the OCI
    /// registry: <c>/v2/{repo}/…</c> must answer <c>401</c> with a token challenge, not a plain <c>404</c>, so that a client
    /// cannot tell a private repository from one that does not exist). <see cref="RepositoryResolutionMiddleware"/> then leaves
    /// the repository alone; the route's own filter calls <see cref="BaGet.Core.IRepositoryContext.Use"/> once it has authorized.
    /// Such a route also <see cref="AuthorizesItself"/>.
    /// </summary>
    public bool ResolvesRepositoryItself { get; }

    internal static RepositoryRouteMetadata For(bool writesOnly, bool authorizesItself = false, RepositoryFormat format = RepositoryFormat.NuGet, bool writesContent = true, bool resolvesRepositoryItself = false)
        => format != RepositoryFormat.NuGet || !writesContent || resolvesRepositoryItself ? new RepositoryRouteMetadata(writesOnly, authorizesItself || resolvesRepositoryItself, format, writesContent, resolvesRepositoryItself)
        : authorizesItself ? SelfAuthorized : writesOnly ? WriteOnly : Instance;
}
