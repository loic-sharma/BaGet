using System.IO;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    public interface IPackageContentClient
    {
        Task<PackageVersions> GetPackageVersionsOrNullAsync(string url);

        Task<Stream> GetPackageContentStreamAsync(string url);

        Task<Stream> GetPackageManifestStreamAsync(string url);
    }
}
