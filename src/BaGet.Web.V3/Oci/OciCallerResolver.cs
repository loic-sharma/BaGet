using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Security;
using Microsoft.AspNetCore.Http;

namespace BaGet.Web.V3.Oci;

/// <summary>
/// Finds out who calls the registry and what that caller may do, in one place for the routes and the token endpoint. A
/// <c>Bearer</c> credential is a registry token first (the <c>docker login</c> flow) and an API key second (a script may send its
/// key that way, as npm does); <c>Basic</c> carries an API key as the password, like every other format. The browser session is
/// never used: a cookie is sent by the browser on its own.
/// </summary>
public class OciCallerResolver
{
    private const string ItemKey = "BaGet.OciCaller";

    private readonly IOciTokenService _tokens;
    private readonly RequestPrincipalResolver _principals;
    private readonly IOciAccess _access;

    /// <param name="tokens">Checks registry tokens.</param>
    /// <param name="principals">Authenticates API keys.</param>
    /// <param name="access">The access rule.</param>
    public OciCallerResolver(IOciTokenService tokens, RequestPrincipalResolver principals, IOciAccess access)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _principals = principals ?? throw new ArgumentNullException(nameof(principals));
        _access = access ?? throw new ArgumentNullException(nameof(access));
    }

    /// <summary>The caller of the request (resolved once).</summary>
    public async Task<OciCaller> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Items.TryGetValue(ItemKey, out var cached) && cached is OciCaller known) return known;

        var caller = await ResolveCoreAsync(context, cancellationToken).ConfigureAwait(false);
        context.Items[ItemKey] = caller;

        return caller;
    }

    private async Task<OciCaller> ResolveCoreAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var bearer = BasicCredentials.ReadBearer(context.Request);
        var refused = false;
        if (bearer != null)
        {
            var claims = _tokens.Validate(bearer);
            if (claims != null) return new OciCaller { Token = claims };

            refused = true;
        }

        var principal = await _principals.ResolveForWriteAsync(context, cancellationToken).ConfigureAwait(false);

        return new OciCaller { Principal = principal, TokenRefused = refused && principal == null };
    }

    /// <summary>
    /// Whether <paramref name="caller"/> may do <paramref name="action"/> to <paramref name="image"/> where a request addressed to
    /// <paramref name="named"/> lands in <paramref name="actual"/> (a push to a virtual repository lands in its deploy repository): a
    /// registry token is checked against the repository the client named, a key against the one that does the work.
    /// </summary>
    public Task<bool> AllowsAsync(OciCaller caller, RepositoryInfo named, RepositoryInfo actual, string image, OciActions action, CancellationToken cancellationToken)
        => caller.Token != null
            ? AllowsAsync(caller, named, image, action, cancellationToken)
            : AllowsAsync(caller, actual, image, action, cancellationToken);

    /// <summary>Whether <paramref name="caller"/> may do <paramref name="action"/> to <paramref name="image"/> of <paramref name="repository"/>.</summary>
    public Task<bool> AllowsAsync(OciCaller caller, RepositoryInfo repository, string image, OciActions action, CancellationToken cancellationToken)
    {
        if (caller.Token != null)
        {
            // A token carries what it was issued for; it never reaches beyond it, whatever the user may do now.
            return Task.FromResult(repository.Format == RepositoryFormat.Docker && caller.Token.Allows(repository.Name, image, action));
        }

        return _access.IsAllowedAsync(caller.Principal, repository, image, action, cancellationToken);
    }
}
