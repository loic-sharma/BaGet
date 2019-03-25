using System.Net;
using System.Threading.Tasks;

namespace BaGet.Core.Services
{
    public interface IUserValidationService
    {
        Task<bool> IsValidUserAsync(NetworkCredential credential);
    }
}
