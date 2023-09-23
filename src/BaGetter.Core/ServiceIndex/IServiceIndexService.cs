using System.Threading;
using System.Threading.Tasks;
using BaGetter.Protocol.Models;

namespace BaGetter.Core
{
    /// <summary>
    /// The NuGet Service Index service, used to discover other resources.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/service-index
    /// </summary>
    public interface IServiceIndexService
    {
        /// <summary>
        /// Get the resources available on this package feed.
        /// See: https://docs.microsoft.com/en-us/nuget/api/service-index#resources
        /// </summary>
        /// <returns>The resources available on this package feed.</returns>
        Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken = default);
    }
}
