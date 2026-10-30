using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// Implements the NuGet Package Content resource in NuGet's V3 protocol.
/// </summary>
public class DefaultPackageContentService : IPackageContentService
{
    private readonly IPackageService _packages;
    private readonly IPackageStorageService _storage;

    public DefaultPackageContentService(
        IPackageService packages,
        IPackageStorageService storage)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<PackageVersionsResponse?> GetPackageVersionsOrNullAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var versions = await _packages.FindPackageVersionsAsync(id, cancellationToken);
        if (!versions.Any())
        {
            return null;
        }

        return new PackageVersionsResponse
        {
            Versions = versions
                .Select(v => v.ToNormalizedString())
                .Select(v => v.ToLowerInvariant())
                .ToList()
        };
    }

    public async Task<Stream?> GetPackageContentStreamOrNullAsync(
        string id,
        NuGetVersion version,
        CancellationToken cancellationToken = default)
    {
        // A remote repository that stores nothing streams the upstream's file through.
        var uncached = await _packages.OpenUncachedAsync(id, version, cancellationToken);
        if (uncached != null) return uncached;

        if (!await _packages.ExistsAsync(id, version, cancellationToken))
        {
            return null;
        }

        await _packages.AddDownloadAsync(id, version, cancellationToken);
        return await _storage.GetPackageStreamAsync(id, version, cancellationToken);
    }

    public async Task<Stream?> GetPackageManifestStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
    {
        using (var uncached = await _packages.OpenUncachedAsync(id, version, cancellationToken))
        {
            if (uncached != null) return ReadNuspec(uncached);
        }

        if (!await _packages.ExistsAsync(id, version, cancellationToken))
        {
            return null;
        }

        return await _storage.GetNuspecStreamAsync(id, version, cancellationToken);
    }

    public async Task<Stream?> GetPackageReadmeStreamOrNullAsync(string? id, NuGetVersion version, CancellationToken cancellationToken = default)
    {
        var package = await _packages.FindPackageOrNullAsync(id, version, cancellationToken);
        if (package == null || !package.HasReadme)
        {
            return null;
        }

        return await _storage.GetReadmeStreamAsync(id, version, cancellationToken);
    }

    public async Task<Stream?> GetPackageIconStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
    {
        var package = await _packages.FindPackageOrNullAsync(id, version, cancellationToken);
        if (package == null || !package.HasEmbeddedIcon)
        {
            return null;
        }

        return await _storage.GetIconStreamAsync(id, version, cancellationToken);
    }

    // The .nuspec at the root of a package, copied to memory so the package stream can be disposed.
    private static Stream? ReadNuspec(Stream package)
    {
        using var archive = new System.IO.Compression.ZipArchive(package, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.IndexOf('/') < 0 && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;

        var copy = new MemoryStream();
        using (var source = entry.Open())
        {
            source.CopyTo(copy);
        }

        copy.Position = 0;

        return copy;
    }
}
