using System.Threading.Tasks;

namespace BaGet.Protocol
{
    public interface IServiceIndexClient
    {
        Task<ServiceIndex> GetServiceIndexAsync(string indexUrl);
    }
}
