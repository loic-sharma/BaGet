using System.Threading.Tasks;

namespace BaGet.Protocol
{
    public interface IRegistrationClient
    {
        Task<RegistrationIndex> GetRegistrationIndexAsync(string indexUrl);

        Task<RegistrationIndexPage> GetRegistrationIndexPageAsync(string pageUrl);

        Task<RegistrationLeaf> GetRegistrationLeafAsync(string leafUrl);
    }
}
