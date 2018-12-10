using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using BaGet.AWS.Configuration;
using BaGet.Core.Services;
using Microsoft.Extensions.Options;

namespace BaGet.AWS
{
    public class S3StorageService : IStorageService
    {
        private const string Separator = "/";
        private readonly string _bucket;
        private readonly string _prefix;
        private readonly AmazonS3Client _client;

        public S3StorageService(IOptions<S3StorageOptions> options, AmazonS3Client client)
        {
            _bucket = options.Value.Bucket;
            _prefix = options.Value.Prefix;
            _client = client;

            if (!string.IsNullOrEmpty(_prefix) && !_prefix.EndsWith(Separator))
                _prefix += Separator;
        }

        private string PrepareKey(string path)
        {
            return _prefix + path.Replace("\\", Separator);
        }

        public async Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            MemoryStream stream = new MemoryStream();

            try
            {
                using (GetObjectResponse res = await _client.GetObjectAsync(_bucket, PrepareKey(path), cancellationToken))
                    await res.ResponseStream.CopyToAsync(stream);

                stream.Seek(0, SeekOrigin.Begin);
            }
            catch (Exception)
            {
                stream.Dispose();

                // TODO
                throw;
            }

            return stream;
        }

        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
        {
            string res = _client.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _bucket,
                Key = PrepareKey(path)
            });

            return Task.FromResult(new Uri(res));
        }

        public async Task<PutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            // TODO: Uploads should be idempotent. This should fail if and only if the blob
            // already exists but has different content.

            using (MemoryStream ms = new MemoryStream())
            {
                await content.CopyToAsync(ms, 4096, cancellationToken);

                ms.Seek(0, SeekOrigin.Begin);

                await _client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = PrepareKey(path),
                    InputStream = ms,
                    ContentType = contentType,
                    AutoResetStreamPosition = false,
                    AutoCloseStream = false
                }, cancellationToken);
            }

            return PutResult.Success;
        }

        public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            await _client.DeleteObjectAsync(_bucket, PrepareKey(path), cancellationToken);
        }

        public Task<IEnumerable<string>> GetPackagePathsAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Not supported for S3StorageService");
        }
    }
}
