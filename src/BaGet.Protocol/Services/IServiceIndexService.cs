using System.Threading.Tasks;

namespace BaGet.Protocol
{
    public interface IServiceIndexService
    {
        Task<string> GetPackageContentUrlAsync();

        Task<string> GetRegistrationUrlAsync();

        Task<string> GetSearchUrlAsync();
    }
}
