using System.Threading.Tasks;
using System.Net;

namespace BaGet.Core.Services
{
    public interface IUserValidationService
    {
        Task<bool> IsValidUserAsync(NetworkCredential credential);
    }
}
