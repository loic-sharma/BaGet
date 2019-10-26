using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    /// <summary>
    /// Stores the content of symbols, also known as PDBs.
    /// </summary>
    public class SymbolStorageService
    {
        private const string SymbolsPathPrefix = "symbols";
        private const string PdbContentType = "binary/octet-stream";

        private readonly IStorageService _storage;

        public SymbolStorageService(IStorageService storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }
        /// <summary>
        /// Persist a portable PDB's content to storage. This operation MUST fail if a PDB
        /// with the same key but different content has already been stored.
        /// </summary>
        /// <param name="fileName">The portable PDB's file name.</param>
        /// <param name="key">The portable PDB's Signature GUID followed by its age.</param>
        /// <param name="pdbStream">The PDB's content stream.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public virtual async Task SavePortablePdbContentAsync(
            string fileName,
            string key,
            Stream pdbStream,
            CancellationToken cancellationToken)
        {
            var path = GetPathForKey(fileName, key);
            var result = await _storage.PutAsync(path, pdbStream, PdbContentType, cancellationToken);

            if (result == StoragePutResult.Conflict)
            {
                throw new InvalidOperationException($"Could not save PDB {fileName} {key} due to conflict");
            }
        }
        /// <summary>
        /// Retrieve a portable PDB's content stream.
        /// </summary>
        /// <param name="fileName">The portable PDB's file name.</param>
        /// <param name="key">The portable PDB's Signature GUID followed by its age.</param>
        /// <returns>The portable PDB's stream, or null if it does not exist.</returns>
        public virtual async Task<Stream> GetPortablePdbContentStreamOrNullAsync(string fileName, string key)
        {
            var path = GetPathForKey(fileName, key);

            try
            {
                return await _storage.GetAsync(path);
            }
            catch
            {
                return null;
            }
        }

        private string GetPathForKey(string filename, string key)
        {
            // Ensure the filename doesn't try to escape out of the current directory.
            var tempPath = Path.GetDirectoryName(Path.GetTempPath());
            var expandedPath = Path.GetDirectoryName(Path.Combine(tempPath, filename));
            
            if (expandedPath != tempPath)
            {
                throw new ArgumentException(nameof(filename));
            }

            if (!key.All(char.IsLetterOrDigit))
            {
                throw new ArgumentException(nameof(key));
            }

            return Path.Combine(
                SymbolsPathPrefix,
                filename.ToLowerInvariant(),
                key.ToLowerInvariant());
        }
    }
}
