using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aliyun.OSS;
using BaGet.Core;
using Microsoft.Extensions.Options;

namespace BaGet.Aliyun
{
    public class AliyunStorageService : IStorageService
    {
        private const string Separator = "/";
        private readonly string _bucket;
        private readonly string _prefix;
        private readonly OssClient _client;

        public AliyunStorageService(IOptionsSnapshot<AliyunStorageOptions> options, OssClient client)
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
            try
            {
                var ossObject = await Task.Factory.FromAsync(_client.BeginGetObject, _client.EndGetObject, _bucket, PrepareKey(path), null);

                return ossObject.ResponseStream;
            }
            catch (Exception)
            {
                // TODO
                throw;
            }
        }

        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
        {
            var uri = _client.GeneratePresignedUri(_bucket, PrepareKey(path));

            return Task.FromResult(uri);
        }

        public async Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            // TODO: Uploads should be idempotent. This should fail if and only if the blob
            // already exists but has different content.

            var metadata = new ObjectMetadata
            {
                ContentType = contentType,
            };

            var putResult = await Task<PutObjectResult>.Factory.FromAsync(_client.BeginPutObject, _client.EndPutObject, _bucket, PrepareKey(path), content, metadata);

            switch (putResult.HttpStatusCode)
            {
                case System.Net.HttpStatusCode.OK:
                    return StoragePutResult.Success;

                // TODO: check sdk documents
                //case System.Net.HttpStatusCode.Conflict:
                //    return StoragePutResult.Conflict;

                //case System.Net.HttpStatusCode.Found:
                //    return StoragePutResult.AlreadyExists;

                default:
                    return StoragePutResult.Success;
            }
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            _client.DeleteObject(_bucket, PrepareKey(path));
            return Task.CompletedTask;
        }
    }
}
