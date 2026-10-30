using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

public interface IAuthenticationService
{
    Task<bool> AuthenticateAsync(string? apiKey, CancellationToken cancellationToken);

    /// <summary>
    /// Who the API key authenticated, or null if it did not. An implementation that only
    /// knows the global key reports every success as <see cref="BaGetPrincipal.Superuser"/>.
    /// </summary>
    async Task<BaGetPrincipal?> AuthenticatePrincipalAsync(string? apiKey, CancellationToken cancellationToken)
        => await AuthenticateAsync(apiKey, cancellationToken) ? BaGetPrincipal.Superuser : null;
}