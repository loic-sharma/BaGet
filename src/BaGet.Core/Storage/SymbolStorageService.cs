using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

public class SymbolStorageService : ISymbolStorageService
{
    private const string SymbolsPathPrefix = "symbols";
    private const string PdbContentType = "binary/octet-stream";

    private readonly IStorageService _storage;
    private readonly IRepositoryContext? _repository;

    /// <summary>
    /// The repository whose symbols are stored and served: the default one keeps <c>symbols/…</c>, any other
    /// <c>repos/{name}/symbols/…</c>, so symbols pushed to one repository are not served from another. Null means
    /// the default repository.
    /// </summary>
    public SymbolStorageService(IStorageService storage, IRepositoryContext? repository = null)
    {
        _repository = repository;
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task SavePortablePdbContentAsync(
        string filename,
        string key,
        Stream pdbStream,
        CancellationToken cancellationToken)
    {
        var path = GetPathForKey(filename, key);
        var result = await _storage.PutAsync(path, pdbStream, PdbContentType, cancellationToken);

        if (result == StoragePutResult.Conflict)
        {
            throw new InvalidOperationException($"Could not save PDB {filename} {key} due to conflict");
        }
    }

    public async Task<Stream?> GetPortablePdbContentStreamOrNullAsync(string filename, string key)
    {
        try
        {
            // An invalid file name or key (too short, not alphanumeric) is a miss, not a server error.
            var path = GetPathForKey(filename, key);

            return await _storage.GetAsync(path);
        }
        catch
        {
            return null;
        }
    }

    public async Task DeletePortablePdbContentAsync(string filename, string key, CancellationToken cancellationToken)
    {
        await _storage.DeleteAsync(GetPathForKey(filename, key), cancellationToken);
    }

    private string GetPathForKey(string filename, string key)
    {
        // Ensure the filename doesn't try to escape out of the current directory.
        var tempPath = Path.GetDirectoryName(Path.GetTempPath());
        #pragma warning disable CS8604
        var expandedPath = Path.GetDirectoryName(Path.Combine(tempPath, filename));
        #pragma warning restore CS8604
            
        if (expandedPath != tempPath)
        {
            throw new ArgumentException(nameof(filename));
        }

        if (key.Length < 32 || !key.All(char.IsLetterOrDigit))
        {
            throw new ArgumentException(nameof(key));
        }

        // The key's first 32 characters are the GUID, the remaining characters are the age.
        // See: https://github.com/dotnet/symstore/blob/98717c63ec8342acf8a07aa5c909b88bd0c664cc/docs/specs/SSQP_Key_Conventions.md#portable-pdb-signature
        // Debuggers should always use the age "ffffffff", however Visual Studio 2019
        // users have reported other age values. We will ignore the age.
        key = key.Substring(0, 32) + "ffffffff";

        return Path.Combine(
            RepositoryStorage.Segments(_repository?.Current ?? RepositoryInfo.Default)
                .Concat(new[] { SymbolsPathPrefix, filename.ToLowerInvariant(), key.ToLowerInvariant() })
                .ToArray());
    }
}