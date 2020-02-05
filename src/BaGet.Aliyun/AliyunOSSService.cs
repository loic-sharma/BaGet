using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aliyun.OSS;
using BaGet.Aliyun.Configuration;
using BaGet.Core;
using Microsoft.Extensions.Options;

namespace BaGet.Aliyun
{
    public class AliyunOSSService : IStorageService
    {
        private const string Separator = "/";
        private readonly string _bucket;
        private readonly string _prefix;
        private readonly OssClient _client;

        public AliyunOSSService(IOptionsSnapshot<AliyunOSSOptions> options, OssClient client)
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
                return await Task.Run<Stream>(() =>
                 {
                     var ossObject = _client.GetObject(_bucket, PrepareKey(path));
                     return ossObject.ResponseStream;
                 });
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

            return await Task<StoragePutResult>.Run(() =>
            {
                var rst = _client.PutObject(_bucket, PrepareKey(path), content, new ObjectMetadata
                {
                    ContentType = contentType,
                });

                if (rst.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    return StoragePutResult.Success;
                }

                return StoragePutResult.Success;
            });
        }

        public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                _client.DeleteObject(_bucket, PrepareKey(path));
            });
        }
    }
}
