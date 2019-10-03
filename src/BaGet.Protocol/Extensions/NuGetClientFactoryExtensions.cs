using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Catalog;
using Microsoft.Extensions.Logging;

namespace BaGet.Protocol
{
    public static class NuGetClientFactoryExtensions
    {
        /// <summary>
        /// Create a new <see cref="CatalogProcessor"/> to discover and download catalog leafs.
        /// Leafs are processed by the <see cref="ICatalogLeafProcessor"/>.
        /// </summary>
        /// <param name="clientFactory">The factory used to create NuGet clients.</param>
        /// <param name="cursor">Cursor to track succesfully processed leafs. Leafs before the cursor are skipped.</param>
        /// <param name="leafProcessor">The leaf processor.</param>
        /// <param name="options">The options to configure catalog processing.</param>
        /// <param name="logger">The logger used for telemetry.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The catalog processor.</returns>
        public static async Task<CatalogProcessor> CreateCatalogProcessorAsync(
            this NuGetClientFactory clientFactory,
            ICursor cursor,
            ICatalogLeafProcessor leafProcessor,
            CatalogProcessorOptions options,
            ILogger<CatalogProcessor> logger,
            CancellationToken cancellationToken = default)
        {
            var catalogClient = await clientFactory.GetCatalogClientAsync(cancellationToken);

            return new CatalogProcessor(
                cursor,
                catalogClient,
                leafProcessor,
                options,
                logger);
        }
    }
}
