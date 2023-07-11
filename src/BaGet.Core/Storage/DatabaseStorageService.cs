using System.Linq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core
{
    public class DatabaseStorageService : IStorageService
    {
        private readonly IPackageContentsContext _context;

        public DatabaseStorageService(IPackageContentsContext context)
        {
            if (context == null) throw new ArgumentException(nameof(context));

            _context = context;
        }

        public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            var contents = await _context.PackageContents.SingleOrDefaultAsync(p => p.Path == path, cancellationToken);
            if (contents != null)
            {
                _context.PackageContents.Remove(contents);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            var contents = await _context.PackageContents.SingleOrDefaultAsync(p => p.Path == path, cancellationToken);
            if (contents == null)
            {
                throw new Exception($"PackageContents record not found for path: {path}");
            }
            var ms = new MemoryStream(contents.Data);
            return ms;
        }

        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            byte[] newData;
            using (var binaryReader = new BinaryReader(content))
            {
                newData = binaryReader.ReadBytes((int)content.Length);
            }

            var existingContents = await _context.PackageContents.SingleOrDefaultAsync(p => p.Path == path, cancellationToken);
            if (existingContents != null)
            {
                return existingContents.Data.SequenceEqual(newData)
                    ? StoragePutResult.AlreadyExists
                    : StoragePutResult.Conflict;
            }

            _context.PackageContents.Add(new PackageContents
            {
                Path = path,
                Data = newData,
            });
            await _context.SaveChangesAsync(cancellationToken);

            return StoragePutResult.Success;
        }
    }
}
