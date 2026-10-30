using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Mvc.Screens;
using BaGet.Identity.Application.UseCases.Account;
using Microsoft.AspNetCore.Http;

namespace BaGet.Frontend.Security;

/// <summary>
/// <see cref="IReauthenticationState"/> over the request's sign-in cookie: whether the session re-authenticated with the external
/// provider within <see cref="AccountSupport.ReauthenticationWindow"/>. Without a request (or a cookie) the answer is false.
/// </summary>
internal sealed class HttpReauthenticationState : IReauthenticationState
{
    private readonly IHttpContextAccessor _accessor;

    public HttpReauthenticationState(IHttpContextAccessor accessor)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    }

    public Task<bool> HasReauthenticatedRecentlyAsync(CancellationToken cancellationToken)
        => _accessor.HttpContext == null
            ? Task.FromResult(false)
            : AccountSupport.HasReauthenticatedRecentlyAsync(_accessor.HttpContext);
}
