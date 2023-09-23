using System.Threading;
using System.Threading.Tasks;

namespace BaGetter.Core
{
    public interface IAuthenticationService
    {
        Task<bool> AuthenticateAsync(string apiKey, CancellationToken cancellationToken);
    }
}
