using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Rest.Azure;

namespace BaGet.Azure.Search
{
    public class AzureSearchBatchIndexer
    {
        /// <summary>
        /// Azure Search accepts batches of up to 1000 documents.
        /// </summary>
        private const int MaxBatchSize = 1000;
        private const int MaxEnqueueAttempts = 5;

        private readonly ISearchIndexClient _indexClient;
        private readonly ConcurrentQueue<IndexAction<KeyedDocument>> _pendingActions;
        private readonly ILogger<AzureSearchBatchIndexer> _logger;

        public AzureSearchBatchIndexer(
            SearchServiceClient searchClient,
            ILogger<AzureSearchBatchIndexer> logger)
        {
            if (searchClient == null) throw new ArgumentNullException(nameof(searchClient));

            _indexClient = searchClient.Indexes.GetClient(PackageDocument.IndexName);
            _pendingActions = new ConcurrentQueue<IndexAction<KeyedDocument>>();

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public virtual bool TryEnqueueIndexAction(IndexAction<KeyedDocument> action)
        {
            if (_pendingActions.Count > (MaxBatchSize * 2))
            {
                return false;
            }

            _pendingActions.Enqueue(action);
            return true;
        }

        public virtual async Task EnqueueIndexActionAsync(
            IndexAction<KeyedDocument> action,
            CancellationToken cancellationToken = default)
        {
            var attempts = 0;
            while (true)
            {
                if (TryEnqueueIndexAction(action))
                {
                    break;
                }

                attempts++;
                if (attempts >= MaxEnqueueAttempts)
                {
                    throw new InvalidOperationException(
                        $"Failed to enqueue index action after {MaxEnqueueAttempts} attempts");
                }

                await PushBatchesAsync(onlyFull: true, cancellationToken);
            }
        }

        public virtual async Task PushBatchesAsync(CancellationToken cancellationToken = default)
        {
            await PushBatchesAsync(onlyFull: false, cancellationToken);
        }

        public virtual async Task PushBatchesAsync(
            bool onlyFull,
            CancellationToken cancellationToken = default)
        {
            while ((onlyFull && _pendingActions.Count >= MaxBatchSize)
                || (!onlyFull && _pendingActions.Count > 0))
            {
                var batch = new List<IndexAction<KeyedDocument>>();

                while (batch.Count < MaxBatchSize
                    && _pendingActions.TryDequeue(out var pendingAction))
                {
                    batch.Add(pendingAction);
                }

                await PushBatchAsync(batch, cancellationToken);
            }
        }

        private async Task PushBatchAsync(
            IReadOnlyList<IndexAction<KeyedDocument>> batch,
            CancellationToken cancellationToken)
        {
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

                await PushBatchAsync(halfA, cancellationToken);
                await PushBatchAsync(halfB, cancellationToken);
            }

            if (indexingResults != null && indexingResults.Any(result => !result.Succeeded))
            {
                throw new InvalidOperationException("Failed to pushed batch of documents documents");
            }
        }
    }
}
