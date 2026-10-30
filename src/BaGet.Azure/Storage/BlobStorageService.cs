using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Options;

namespace BaGet.Azure;

// See: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Services/CloudBlobCoreFileStorageService.cs
public partial class BlobStorageService : IStreamingStorageService, IListableStorageService
{
    private readonly CloudBlobContainer _container;
    private readonly TimeSpan _downloadUriExpiry;

    public BlobStorageService(IOptionsSnapshot<AzureBlobStorageOptions> options, CloudBlobContainer container)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        _container = container ?? throw new ArgumentNullException(nameof(container));
        _downloadUriExpiry = options.Value.DownloadUriExpiry;

        if (_downloadUriExpiry <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"The {nameof(AzureBlobStorageOptions.DownloadUriExpiry)} option must be greater than zero",
                nameof(options));
        }
    }

    public async Task<Stream> GetAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _container
                .GetBlockBlobReference(path)
                .OpenReadAsync(cancellationToken);
        }
        catch (StorageException e) when (e.RequestInformation?.HttpStatusCode == 404)
        {
            // The one "missing content" exception of IStorageService.GetAsync, as FileStorageService throws.
            throw new FileNotFoundException("The blob '" + path + "' does not exist.", path, e);
        }
    }

    public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlockBlobReference(path);
        var accessPolicy = new SharedAccessBlobPolicy
        {
            SharedAccessExpiryTime = DateTimeOffset.UtcNow.Add(_downloadUriExpiry),
            Permissions = SharedAccessBlobPermissions.Read
        };

        var signature = blob.GetSharedAccessSignature(accessPolicy);
        var result = new Uri(blob.Uri, signature);

        return Task.FromResult(result);
    }

    public async Task<StoragePutResult> PutAsync(
        string path,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlockBlobReference(path);
        var condition = AccessCondition.GenerateIfNotExistsCondition();

        blob.Properties.ContentType = contentType;

        try
        {
            await blob.UploadFromStreamAsync(
                content,
                condition,
                options: null,
                operationContext: null,
                cancellationToken: cancellationToken);

            return StoragePutResult.Success;
        }
        catch (StorageException e) when (e.IsAlreadyExistsException())
        {
            using (var targetStream = await blob.OpenReadAsync(cancellationToken))
            {
                content.Position = 0;
                return content.Matches(targetStream)
                    ? StoragePutResult.AlreadyExists
                    : StoragePutResult.Conflict;
            }
        }
    }

    /// <summary>
    /// Every blob under a prefix, paged until the container says there is no more. The
    /// sweep of abandoned staged uploads is the only caller, and without this a container
    /// would keep them forever.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        // A prefix is a path segment, not a string prefix: "staging" must not also match
        // a "staging-archive" container entry.
        var prefix = pathPrefix.TrimEnd('/') + "/";
        var paths = new List<string>();
        BlobContinuationToken? continuation = null;

        do
        {
            var segment = await _container.ListBlobsSegmentedAsync(
                prefix,
                useFlatBlobListing: true,
                blobListingDetails: BlobListingDetails.None,
                maxResults: null,
                currentToken: continuation,
                options: null,
                operationContext: null,
                cancellationToken: cancellationToken);

            foreach (var item in segment.Results)
            {
                // The blob's name is already the path IStorageService takes, relative to
                // the container.
                if (item is CloudBlob blob) paths.Add(blob.Name);
            }

            continuation = segment.ContinuationToken;
        }
        while (continuation != null);

        return paths;
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        await _container
            .GetBlockBlobReference(path)
            .DeleteIfExistsAsync(cancellationToken);
    }
}