using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    internal interface IAsyncUrlGenerator
    {
        Task<string> GetPackageContentResourceUrlAsync(CancellationToken cancellationToken = default);
        Task<string> GetPackageMetadataResourceUrlAsync(CancellationToken cancellationToken = default);
        Task<string> GetPackagePublishResourceUrlAsync(CancellationToken cancellationToken = default);
        Task<string> GetSymbolPublishResourceUrlAsync(CancellationToken cancellationToken = default);
        Task<string> GetSearchResourceUrlAsync(CancellationToken cancellationToken = default);
        Task<string> GetAutocompleteResourceUrlAsync(CancellationToken cancellationToken = default);

        Task<string> GetRegistrationIndexUrlAsync(string id, CancellationToken cancellationToken = default);
        Task<string> GetRegistrationPageUrlAsync(string id, NuGetVersion lower, NuGetVersion upper, CancellationToken cancellationToken = default);
        Task<string> GetRegistrationLeafUrlAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default);

        Task<string> GetPackageVersionsUrlAsync(string id, CancellationToken cancellationToken = default);
        Task<string> GetPackageDownloadUrlAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default);
        Task<string> GetPackageManifestDownloadUrlAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default);
    }
}
