using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;

namespace BaGet.Database.RavenDb
{
    public class RavenStorage : IStorageService
    {
        private readonly IAsyncDocumentSession _session;
        private readonly IDocumentStore _store;

        public RavenStorage(
            IAsyncDocumentSession session,
            IDocumentStore store)
        {
            _session = session;
            _store = store;
        }

        public async Task<Stream> GetAsync(Blob blob, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attachmentResult = await _session.Advanced.Attachments.GetAsync(
                blob.PackageId, blob.Name, cancellationToken);
            return attachmentResult.Stream;
        }

        public async Task<StoragePutResult> PutAsync(
            Blob blob,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));
            if (string.IsNullOrEmpty(contentType))
                throw new ArgumentException("Content type is required", nameof(contentType));

            cancellationToken.ThrowIfCancellationRequested();

            var session = _store.OpenAsyncSession();
            var packageEntity = await session.LoadAsync<Package>(blob.PackageId, cancellationToken);
            if (packageEntity is null)
                throw new ArgumentNullException(nameof(packageEntity));

            var exists = await _session.Advanced.Attachments.ExistsAsync(
                blob.PackageId, blob.Name, cancellationToken);
            if (exists)
                return StoragePutResult.Conflict;

            _session.Advanced.Attachments.Store(packageEntity, blob.Name, content, contentType);
            await session.SaveChangesAsync(cancellationToken);
            return StoragePutResult.Success;
        }

        public async Task DeleteAsync(Blob blob, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _store.OpenAsyncSession();
            session.Advanced.Attachments.Delete(blob.PackageId, blob.Name);
            await session.SaveChangesAsync(cancellationToken);
        }
    }
}
