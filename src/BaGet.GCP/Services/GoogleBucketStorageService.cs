using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Services;
using BaGet.GCP.Configuration;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;

namespace BaGet.GCP.Services
{
    public class GoogleBucketStorageService : IStorageService
    {
        private readonly string _bucketName;

        public GoogleBucketStorageService(IOptionsSnapshot<GoogleBucketStorageOptions> options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _bucketName = options.Value.BucketName;
        }

        public async Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            var storage = await StorageClient.CreateAsync();
            var stream = new MemoryStream();
            await storage.DownloadObjectAsync(_bucketName, path, stream, cancellationToken: cancellationToken);
            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }

        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Uri(path));
        }

        public async Task<PutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            var storage = await StorageClient.CreateAsync();
            try
            {
                await storage.GetObjectAsync(_bucketName, path, cancellationToken: cancellationToken);
                return PutResult.AlreadyExists;
            }
            catch (GoogleApiException e)
            {
                if (e.HttpStatusCode != HttpStatusCode.NotFound)
                    throw;

                await storage.UploadObjectAsync(_bucketName, path, null, content, cancellationToken: cancellationToken);
                return PutResult.Success;
            }
        }

        public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            var storage = await StorageClient.CreateAsync();
            var obj = await storage.GetObjectAsync(_bucketName, path, cancellationToken: cancellationToken);
            await storage.DeleteObjectAsync(obj, cancellationToken: cancellationToken);
        }
    }
}
