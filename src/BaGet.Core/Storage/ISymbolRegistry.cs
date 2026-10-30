using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// The PDBs a package version's symbol package contributed, so they can be listed and deleted per version.
/// Rows exist only for symbols indexed after the registry was introduced: older PDBs are keyed by file and key
/// alone in storage, which does not say which package they came from, and cannot be listed per package.
/// Operates on the current repository. A database without relational tables (Azure Table) records nothing.
/// </summary>
public interface ISymbolRegistry
{
    /// <summary>Whether this database can record symbols (false for Azure Table).</summary>
    bool IsSupported { get; }

    /// <summary>Records PDBs for a version; an entry already recorded is left alone.</summary>
    Task RecordAsync(string packageId, NuGetVersion version, IEnumerable<SymbolReference> symbols, CancellationToken cancellationToken);

    /// <summary>The PDBs recorded for a version, ordered by file name.</summary>
    Task<IReadOnlyList<SymbolReference>> ListAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the PDBs recorded for a version (database rows and stored content). Content another version of
    /// the repository still records is kept.
    /// </summary>
    /// <returns>How many PDBs were recorded for the version and are now gone from it.</returns>
    Task<int> DeleteAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken);
}

/// <summary>A PDB as the symbol server addresses it.</summary>
/// <param name="FileName">The PDB's file name.</param>
/// <param name="Key">Signature GUID followed by the age.</param>
public readonly record struct SymbolReference(string FileName, string Key);
