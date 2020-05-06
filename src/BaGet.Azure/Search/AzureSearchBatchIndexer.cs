using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Rest.Azure;

namespace BaGet.Azure
{
    public class AzureSearchBatchIndexer
    {
        /// <summary>
        /// Azure Search accepts batches of up to 1000 documents.
        /// </summary>
        public const int MaxBatchSize = 1000;

        private readonly ISearchIndexClient _indexClient;
        private readonly ILogger<AzureSearchBatchIndexer> _logger;

        public AzureSearchBatchIndexer(
            SearchServiceClient searchClient,
            ILogger<AzureSearchBatchIndexer> logger)
        {
            if (searchClient == null) throw new ArgumentNullException(nameof(searchClient));

            _indexClient = searchClient.Indexes.GetClient(PackageDocument.IndexName);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task IndexAsync(
            IReadOnlyList<IndexAction<KeyedDocument>> batch,
            CancellationToken cancellationToken)
        {
            if (batch.Count > MaxBatchSize)
            {
                throw new ArgumentException(
                    $"Batch cannot have more than {MaxBatchSize} elements",
                    nameof(batch));
            }

            IList<IndexingResult> indexingResults = null;
            Exception innerException = null;

            try
            {
                await _indexClient.Documents.IndexAsync(
                    IndexBatch.New(batch),
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Pushed batch of {DocumentCount} documents", batch.Count);

            }
            catch (IndexBatchException ex)
            {
                _logger.LogError(ex, "An exception was thrown when pushing batch of documents");
                indexingResults = ex.IndexingResults;
                innerException = ex;
            }
            catch (CloudException ex) when (ex.Response.StatusCode == HttpStatusCode.RequestEntityTooLarge && batch.Count > 1)
            {
                var halfCount = batch.Count / 2;
                var halfA = batch.Take(halfCount).ToList();
                var halfB = batch.Skip(halfCount).ToList();

                _logger.LogWarning(
                    0,
                    ex,
                    "The request body for a batch of {BatchSize} was too large. Splitting into two batches of size " +
                    "{HalfA} and {HalfB}.",
                    batch.Count,
                    halfA.Count,
                    halfB.Count);

                await IndexAsync(halfA, cancellationToken);
                await IndexAsync(halfB, cancellationToken);
            }

            if (indexingResults != null && indexingResults.Any(result => !result.Succeeded))
            {
                throw new InvalidOperationException("Failed to pushed batch of documents documents");
            }
        }
    }
}
