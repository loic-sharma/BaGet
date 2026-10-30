using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using BaGet.Aws;
using BaGet.Azure;
using BaGet.Core;
using BaGet.Gcp;
using Google.Cloud.Storage.V1;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Options;

namespace BaGet.Storage.Tests;

internal sealed class Snapshot<T> : IOptionsSnapshot<T> where T : class
{
    public Snapshot(T value) => Value = value;

    public T Value { get; }

    public T Get(string? name) => Value;
}

/// <summary>The file system store, in-process.</summary>
public sealed class FileSystemStreamingTests : StreamingStorageContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BaGetStorage", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    protected override Task<IStreamingStorageService> CreateStoreAsync()
        => Task.FromResult<IStreamingStorageService>(new FileStorageService(new Snapshot<FileSystemStorageOptions>(new FileSystemStorageOptions { Path = _root })));
}

/// <summary>Azure block blobs against Azurite.</summary>
public sealed class AzureBlobStreamingTests : StreamingStorageContract
{
    protected override async Task<IStreamingStorageService> CreateStoreAsync()
    {
        var endpoint = await StorageContainers.AzuriteBlobEndpointAsync();
        var account = CloudStorageAccount.Parse(
            $"DefaultEndpointsProtocol=http;AccountName={StorageContainers.AzuriteAccount};AccountKey={StorageContainers.AzuriteKey};BlobEndpoint={endpoint};");
        var container = account.CreateCloudBlobClient().GetContainerReference("t" + Guid.NewGuid().ToString("N"));
        await container.CreateIfNotExistsAsync(Token);

        return new BlobStorageService(new Snapshot<AzureBlobStorageOptions>(new AzureBlobStorageOptions()), container);
    }
}

/// <summary>S3 multipart against S3Mock.</summary>
public sealed class S3StreamingTests : StreamingStorageContract
{
    protected override async Task<IStreamingStorageService> CreateStoreAsync()
    {
        var client = new AmazonS3Client(
            new BasicAWSCredentials(StorageContainers.S3AccessKey, StorageContainers.S3SecretKey),
            new AmazonS3Config
            {
                ServiceURL = await StorageContainers.S3UrlAsync(),
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
        var bucket = "t" + Guid.NewGuid().ToString("N");
        await client.PutBucketAsync(bucket, Token);

        return new S3StorageService(new Snapshot<S3StorageOptions>(new S3StorageOptions { Bucket = bucket, Prefix = "store" }), client);
    }
}

internal sealed class NoReuseHttpClientFactory : Google.Apis.Http.HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(Google.Apis.Http.CreateHttpClientArgs args)
        => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.Zero, PooledConnectionIdleTimeout = TimeSpan.Zero, UseCookies = false };
}

/// <summary>Google Cloud Storage resumable sessions against a Cloud Storage emulator.</summary>
public sealed class GoogleCloudStreamingTests : StreamingStorageContract
{
    // oittaa/gcp-storage-emulator ignores DELETE on a session and ifGenerationMatch on a copy; both are covered by the service's contract.
    protected override bool CancelledSessionsAreGone => false;

    protected override bool ConditionalCopyIsEnforced => false;

    protected override async Task<IStreamingStorageService> CreateStoreAsync()
    {
        var url = await StorageContainers.GcsUrlAsync();
        // The emulator is a single-threaded HTTP/1.0 Python server that now and then drops a connection a client reuses: no reuse.
        Task<StorageClient> Factory() => new StorageClientBuilder
        {
            BaseUri = url + "/storage/v1/",
            UnauthenticatedAccess = true,
            HttpClientFactory = new NoReuseHttpClientFactory(),
        }.BuildAsync();

        var bucket = "t" + Guid.NewGuid().ToString("N");
        using (var admin = await Factory())
        {
            await admin.CreateBucketAsync("project", bucket, cancellationToken: Token);
        }

        return new GoogleCloudStorageService(new Snapshot<GoogleCloudStorageOptions>(new GoogleCloudStorageOptions { BucketName = bucket }), Factory);
    }
}
