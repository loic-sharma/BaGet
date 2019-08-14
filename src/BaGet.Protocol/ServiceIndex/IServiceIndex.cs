using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    /// <summary>
    /// The NuGet Service Index resource, used to discover other resources.
    /// See: https://docs.microsoft.com/en-us/nuget/api/service-index
    /// </summary>
    public interface IServiceIndex
    {
        /// <summary>
        /// Get the resources available on this package feed.
        /// See: https://docs.microsoft.com/en-us/nuget/api/service-index#resources
        /// </summary>
        /// <returns>The resources available on this package feed.</returns>
        Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken = default);
    }
}
