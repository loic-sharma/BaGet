using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// <see cref="IPackageService"/> that answers for a virtual repository from its members and for any other repository
/// from <see cref="PackageService"/>. Versions are the union of the members' (each version from the first member that has
/// it, so the registration index and the version list of a package that lives in two members show every version); a
/// single version is answered by the first readable member that has it, asking a remote member's upstream when it must.
/// </summary>
public sealed class VirtualPackageService : IPackageService
{
    private readonly PackageService _inner;
    private readonly VirtualRepositoryReader _reader;

    public VirtualPackageService(PackageService inner, VirtualRepositoryReader reader)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        if (!_reader.IsVirtual) return await _inner.FindPackageVersionsAsync(id, cancellationToken).ConfigureAwait(false);

        var all = await _reader.AllAsync(
            id,
            sp => sp.GetRequiredService<PackageService>().FindPackageVersionsAsync(id, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return all.SelectMany(r => r.Value).Distinct().ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Package>> FindPackagesAsync(string id, CancellationToken cancellationToken)
    {
        if (!_reader.IsVirtual) return await _inner.FindPackagesAsync(id, cancellationToken).ConfigureAwait(false);

        var all = await _reader.AllAsync(
            id,
            sp => sp.GetRequiredService<PackageService>().FindPackagesAsync(id, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return VirtualRepositoryReader.Union(all.Select(r => r.Value));
    }

    /// <inheritdoc/>
    public async Task<Package?> FindPackageOrNullAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (!_reader.IsVirtual) return await _inner.FindPackageOrNullAsync(id, version, cancellationToken).ConfigureAwait(false);

        return await _reader.FirstAsync(
            id,
            sp => sp.GetRequiredService<PackageService>().FindPackageOrNullAsync(id, version, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (!_reader.IsVirtual) return await _inner.ExistsAsync(id, version, cancellationToken).ConfigureAwait(false);

        var found = await _reader.FirstAsync<object>(
            id,
            async sp => await sp.GetRequiredService<PackageService>().ExistsAsync(id, version, cancellationToken).ConfigureAwait(false) ? true : null,
            cancellationToken).ConfigureAwait(false);

        return found != null;
    }

    /// <inheritdoc/>
    public async Task AddDownloadAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (!_reader.IsVirtual)
        {
            await _inner.AddDownloadAsync(packageId, version, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The member that holds the version counts the download; nothing is fetched to find it.
        foreach (var member in await _reader.ReadableMembersAsync(packageId, cancellationToken).ConfigureAwait(false))
        {
            var held = await _reader.InMemberAsync(
                member,
                sp => sp.GetRequiredService<IPackageDatabase>().ExistsAsync(packageId, version, cancellationToken)).ConfigureAwait(false);
            if (!held) continue;

            await _reader.InMemberAsync(
                member,
                async sp =>
                {
                    await sp.GetRequiredService<PackageService>().AddDownloadAsync(packageId, version, cancellationToken).ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);

            return;
        }
    }

    /// <inheritdoc/>
    public async Task<Stream?> OpenUncachedAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (!_reader.IsVirtual) return await _inner.OpenUncachedAsync(id, version, cancellationToken).ConfigureAwait(false);

        foreach (var member in await _reader.ReadableMembersAsync(id, cancellationToken).ConfigureAwait(false))
        {
            var stream = await _reader.InMemberAsync(
                member,
                async sp =>
                {
                    var packages = sp.GetRequiredService<PackageService>();
                    if (id == null || !await packages.ExistsAsync(id, version, cancellationToken).ConfigureAwait(false)) return (found: false, stream: (Stream?)null);

                    return (found: true, stream: await packages.OpenUncachedAsync(id, version, cancellationToken).ConfigureAwait(false));
                }).ConfigureAwait(false);

            // The first member that has the version answers, even when it stores the package and so streams nothing.
            if (stream.found) return stream.stream;
        }

        return null;
    }
}

/// <summary>
/// <see cref="IPackageContentService"/> for a virtual repository: downloads, nuspec, readme and icon come from the first
/// readable member that has the version (its own content service, so a remote member fetches and caches as it does when
/// read directly); the version list is the union of the members'.
/// </summary>
public sealed class VirtualPackageContentService : IPackageContentService
{
    private readonly DefaultPackageContentService _inner;
    private readonly VirtualRepositoryReader _reader;

    public VirtualPackageContentService(DefaultPackageContentService inner, VirtualRepositoryReader reader)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <inheritdoc/>
    public async Task<PackageVersionsResponse?> GetPackageVersionsOrNullAsync(string packageId, CancellationToken cancellationToken)
    {
        if (!_reader.IsVirtual) return await _inner.GetPackageVersionsOrNullAsync(packageId, cancellationToken).ConfigureAwait(false);

        var all = await _reader.AllAsync(
            packageId,
            sp => sp.GetRequiredService<PackageService>().FindPackageVersionsAsync(packageId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var versions = all.SelectMany(r => r.Value).Distinct().ToList();
        if (versions.Count == 0) return null;

        return new PackageVersionsResponse
        {
            Versions = versions.Select(v => v.ToNormalizedString().ToLowerInvariant()).ToList(),
        };
    }

    /// <inheritdoc/>
    public Task<Stream?> GetPackageContentStreamOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken)
        => _reader.IsVirtual
            ? _reader.FirstAsync(packageId, sp => sp.GetRequiredService<DefaultPackageContentService>().GetPackageContentStreamOrNullAsync(packageId, packageVersion, cancellationToken), cancellationToken)
            : _inner.GetPackageContentStreamOrNullAsync(packageId, packageVersion, cancellationToken);

    /// <inheritdoc/>
    public Task<Stream?> GetPackageManifestStreamOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken)
        => _reader.IsVirtual
            ? _reader.FirstAsync(packageId, sp => sp.GetRequiredService<DefaultPackageContentService>().GetPackageManifestStreamOrNullAsync(packageId, packageVersion, cancellationToken), cancellationToken)
            : _inner.GetPackageManifestStreamOrNullAsync(packageId, packageVersion, cancellationToken);

    /// <inheritdoc/>
    public Task<Stream?> GetPackageReadmeStreamOrNullAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
        => _reader.IsVirtual
            ? _reader.FirstAsync(id, sp => sp.GetRequiredService<DefaultPackageContentService>().GetPackageReadmeStreamOrNullAsync(id, version, cancellationToken), cancellationToken)
            : _inner.GetPackageReadmeStreamOrNullAsync(id, version, cancellationToken);

    /// <inheritdoc/>
    public Task<Stream?> GetPackageIconStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        => _reader.IsVirtual
            ? _reader.FirstAsync(id, sp => sp.GetRequiredService<DefaultPackageContentService>().GetPackageIconStreamOrNullAsync(id, version, cancellationToken), cancellationToken)
            : _inner.GetPackageIconStreamOrNullAsync(id, version, cancellationToken);
}

/// <summary>
/// <see cref="ISymbolStorageService"/> for a virtual repository: a PDB is served from the first readable member that
/// stores it (the request names a file and a key, not a package, so only the member-level Read applies). Saving and
/// deleting are never done through a virtual repository (a push is redirected to its deploy repository first).
/// </summary>
public sealed class VirtualSymbolStorageService : ISymbolStorageService
{
    private readonly SymbolStorageService _inner;
    private readonly VirtualRepositoryReader _reader;

    public VirtualSymbolStorageService(SymbolStorageService inner, VirtualRepositoryReader reader)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <inheritdoc/>
    public Task SavePortablePdbContentAsync(string file, string key, Stream pdbStream, CancellationToken cancellationToken)
        => _inner.SavePortablePdbContentAsync(file, key, pdbStream, cancellationToken);

    /// <inheritdoc/>
    public Task<Stream?> GetPortablePdbContentStreamOrNullAsync(string file, string key)
        => _reader.IsVirtual
            ? _reader.FirstAsync(null, sp => sp.GetRequiredService<SymbolStorageService>().GetPortablePdbContentStreamOrNullAsync(file, key), CancellationToken.None)
            : _inner.GetPortablePdbContentStreamOrNullAsync(file, key);

    /// <inheritdoc/>
    public Task DeletePortablePdbContentAsync(string file, string key, CancellationToken cancellationToken)
        => _inner.DeletePortablePdbContentAsync(file, key, cancellationToken);
}
