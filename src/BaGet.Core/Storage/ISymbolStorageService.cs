using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    /// <summary>
    /// Stores the content of symbols, also known as PDBs.
    /// </summary>
    public interface ISymbolStorageService
    {
        /// <summary>
        /// Persist a portable PDB's content to storage. This operation MUST fail if a PDB
        /// with the same key but different content has already been stored.
        /// </summary>
        /// <param name="file">The portable PDB's file name.</param>
        /// <param name="key">The portable PDB's Signature GUID followed by its age.</param>
        /// <param name="pdbStream">The PDB's content stream.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task SavePortablePdbContentAsync(
            string file,
            string key,
            Stream pdbStream,
            CancellationToken cancellationToken);

        /// <summary>
        /// Retrieve a portable PDB's content stream.
        /// </summary>
        /// <param name="file">The portable PDB's file name.</param>
        /// <param name="key">The portable PDB's Signature GUID followed by its age.</param>
        /// <returns>The portable PDB's stream, or null if it does not exist.</returns>
        Task<Stream> GetPortablePdbContentStreamOrNullAsync(string file, string key);
    }
}
