namespace BaGet.Identity.Application.Models.Packages;

/// <summary>An indexed PDB of a version.</summary>
/// <param name="FileName">The PDB file name.</param>
/// <param name="Key">Its signature key.</param>
public sealed record PackageManageSymbol(string FileName, string Key);
