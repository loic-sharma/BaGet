using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol;
namespace BaGet.Core.Services
{
    public interface IRegistrationIndexService
    {
        Task<RegistrationIndex> Get(string id, IUrlExtension Url, CancellationToken cancellationToken);
    }
}
