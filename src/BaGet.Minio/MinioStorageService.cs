using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.Options;
using Minio;

namespace BaGet.Minio
{
    public class MinioStorageService : IStorageService
    {
        private const string Separator = "/";
        private readonly string _bucket;
        private readonly string _prefix;
        private readonly MinioClient _client;

        public MinioStorageService(IOptionsSnapshot<MinioStorageOptions> options, MinioClient client)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _bucket = options.Value.Bucket;
            _prefix = options.Value.Prefix;
            _client = client ?? throw new ArgumentNullException(nameof(client));

            if (!string.IsNullOrEmpty(_prefix) && !_prefix.EndsWith(Separator))
                _prefix += Separator;
        }

        private string PrepareKey(string path)
        {
            return _prefix + path.Replace("\\", Separator);
        }

        public async Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            var stream = new MemoryStream();

            try
            {
                await _client.GetObjectAsync(_bucket, PrepareKey(path), s => s.CopyTo(stream),
                    cancellationToken: cancellationToken);

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
            var url = _client.PresignedGetObjectAsync(_bucket, PrepareKey(path), 60 * 60);
            return url.ContinueWith(t => new Uri(t.Result), cancellationToken);
        }

        public async Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            // TODO: Uploads should be idempotent. This should fail if and only if the blob
            // already exists but has different content.
            var objName = PrepareKey(path);

            await _client.PutObjectAsync(_bucket, objName, content, content.Length,
                cancellationToken: cancellationToken);

            return StoragePutResult.Success;
        }

        public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            await _client.RemoveObjectAsync(_bucket, PrepareKey(path), cancellationToken);
        }
    }
}
