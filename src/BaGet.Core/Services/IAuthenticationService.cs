using System.Threading.Tasks;

namespace BaGet.Core.Services
{
    public interface IAuthenticationService
    {
        Task<bool> AuthenticateAsync(string apiKey);
    }
}
