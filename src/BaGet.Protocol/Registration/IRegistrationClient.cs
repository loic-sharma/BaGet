using System.Threading.Tasks;

namespace BaGet.Protocol
{
    public interface IRegistrationClient
    {
        /// <summary>
        /// Attempt to get a package's registration index, if it exists.
        /// </summary>
        /// <param name="indexUrl">The url to load the registration index from</param>
        /// <returns>The package's registration index, or null if the package does not exist</returns>
        Task<RegistrationIndex> GetRegistrationIndexOrNullAsync(string indexUrl);

        Task<RegistrationIndexPage> GetRegistrationIndexPageAsync(string pageUrl);

        Task<RegistrationLeaf> GetRegistrationLeafAsync(string leafUrl);
    }
}
