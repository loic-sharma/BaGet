using BaGet.Core;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>A package ID in one repository that only one user owns.</summary>
/// <param name="RepositoryKey">The repository's key.</param>
/// <param name="RepositoryName">The repository's name (<see cref="Repository.DefaultName"/> for the default).</param>
/// <param name="PackageId">The package ID.</param>
/// <param name="Format">The repository's format (an npm package is unpublished, a NuGet package deleted).</param>
public record SoleOwnedPackage(int RepositoryKey, string RepositoryName, string? PackageId, RepositoryFormat Format = RepositoryFormat.NuGet)
{
    /// <summary>The ID for the default repository, <c>ID (name)</c> for any other, for messages that mix repositories.</summary>
    public string Label => RepositoryKey == Repository.DefaultKey ? PackageId ?? string.Empty : $"{PackageId} ({RepositoryName})";
}
