using System.Threading.Tasks;
using System.Net;

namespace BaGet.Core.Services
{
    public class UserValidationService : IUserValidationService
    {
        Task<bool> IUserValidationService.IsValidUserAsync(NetworkCredential credential)
        {
            return Validator(credential);
        }
        public System.Func<NetworkCredential, Task<bool>> Validator { get; set; }
    }
}
