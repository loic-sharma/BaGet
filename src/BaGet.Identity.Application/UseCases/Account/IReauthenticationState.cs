using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>
/// Whether the session re-authenticated with the external provider a moment ago (the proof of presence a sensitive action asks of an
/// account that has no password). It reads the sign-in cookie, so the host implements it (<c>BaGet.Identity</c>); a use case only asks.
/// Without a session (an API key, an anonymous caller) the answer is false.
/// </summary>
public interface IReauthenticationState
{
    /// <summary>Whether the session re-authenticated within the last few minutes.</summary>
    Task<bool> HasReauthenticatedRecentlyAsync(CancellationToken cancellationToken);
}
