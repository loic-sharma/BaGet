using System;

namespace BaGet.Core;

/// <summary>
/// Records that a portable PDB came from a package version's symbol package (.snupkg). Symbols are stored and
/// served by PDB (<c>symbols/{file}/{key}</c>), not by package, so without this row a version's symbols cannot
/// be listed or deleted. Written when the symbol package is indexed; PDBs indexed before this table existed
/// have no row and stay served but unlisted (see <see cref="ISymbolRegistry"/>).
/// </summary>
public class PackageSymbol : IRepositoryScoped
{
    public int Key { get; set; }

    /// <summary>
    /// The owning repository; see <see cref="IRepositoryScoped"/>.
    /// </summary>
    public int RepositoryKey { get; set; }

    /// <summary>The package ID, lower-cased.</summary>
    public string? PackageId { get; set; }

    /// <summary>The normalized package version, lower-cased.</summary>
    public string? PackageVersion { get; set; }

    /// <summary>The PDB's file name, lower-cased, as in its storage path and symbol request.</summary>
    public string? FileName { get; set; }

    /// <summary>The PDB's symbol key: signature GUID plus the fixed age, as indexed.</summary>
    public string? SymbolKey { get; set; }

    public DateTime Created { get; set; }
}
