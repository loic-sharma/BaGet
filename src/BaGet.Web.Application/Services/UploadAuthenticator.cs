using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Security;

namespace BaGet.Web.Application.Services;

/// <summary>
/// Who is uploading through a browser form (NuGet, npm, Maven). A caller that authenticated with an API key (JSON API) is that key, within its scopes
/// and repository limits. With accounts on it is the signed-in session; with accounts off there is no session to trust, so a pasted API key
/// authenticates instead - exactly as the push endpoints require one.
/// </summary>
public sealed class UploadAuthenticator
{
    private readonly IOptions<AccountsOptions> _accounts;
    private readonly IAuthenticationService _authentication;
    private readonly IServiceProvider _services;

    /// <summary>Creates the authenticator.</summary>
    public UploadAuthenticator(IOptions<AccountsOptions> accounts, IAuthenticationService authentication, IServiceProvider services)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>The uploader, or null when nothing authenticates the request.</summary>
    /// <param name="user">The request's user (the session, or the owner of the key).</param>
    /// <param name="key">The API key that authenticated a JSON API request, or null.</param>
    /// <param name="apiKey">The API key pasted into the form (used only while accounts are off).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<BaGetPrincipal?> AuthenticateAsync(ClaimsPrincipal user, BaGetPrincipal? key, string? apiKey, CancellationToken cancellationToken)
    {
        if (key != null) return key;

        if (!_accounts.Value.Enabled)
        {
            return await _authentication.AuthenticatePrincipalAsync(apiKey, cancellationToken);
        }

        // Resolved per request: the factory is only registered while accounts are on.
        var factory = _services.GetService<SessionPrincipalFactory>();

        return factory == null ? null : await factory.CreateAsync(user);
    }
}
