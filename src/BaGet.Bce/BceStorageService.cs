using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaiduBce;
using BaiduBce.Auth;
using BaiduBce.Services.Bos;
using BaiduBce.Services.Bos.Model;
using BaGet.Core;
using Microsoft.Extensions.Options;

namespace BaGet.Bce
{
    public class BceStorageService : IStorageService
    {
        private const string Separator = "/";
        private readonly string _bucket;
        private readonly string _prefix;
        private readonly BosClient _client;

        public BceStorageService(IOptionsSnapshot<BceStorageOptions> options, BosClient client)
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

        public Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                var bosObject = _client.GetObject(_bucket, PrepareKey(path));
                return Task.FromResult(bosObject.ObjectContent);
            }
            catch (Exception)
            {
                // TODO
                throw;
            }
        }

        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
        {
            var uri = _client.GeneratePresignedUrl(_bucket, PrepareKey(path), 1800);

            return Task.FromResult(uri);
        }

        public Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            // TODO: Uploads should be idempotent. This should fail if and only if the blob
            // already exists but has different content.

            var metadata = new ObjectMetadata();
            metadata.ContentType = contentType;

            var putResult = _client.PutObject(_bucket, PrepareKey(path), content, metadata);

            if (!string.IsNullOrEmpty(putResult.ETAG)) {
                return Task.FromResult(StoragePutResult.Success);
            } else {
                return Task.FromResult(StoragePutResult.Conflict);
            }
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            _client.DeleteObject(_bucket, PrepareKey(path));
            return Task.CompletedTask;
        }
    }
}
