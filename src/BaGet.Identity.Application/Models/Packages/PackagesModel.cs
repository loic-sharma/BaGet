using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>
/// The package IDs a user owns, directly or through an organization, summarised per repository: the rows of the My packages
/// and the administrators' package screens. Ownership is per repository, so the IDs are grouped by the repositories the user
/// may read (<see cref="IRepositoryAccess"/>); a repository where the user owns nothing is not listed.
/// </summary>
public static class PackagesModel
{
    /// <summary>
    /// Every owned ID's versions, in one query, projected to the four columns the page
    /// shows. The page cost must not grow with how successful a publisher the user is:
    /// a per-ID <see cref="IPackageDatabase.FindAsync(string, bool, CancellationToken)"/>
    /// would be six round trips each - it eagerly loads the dependencies, package types,
    /// target frameworks, deprecation and vulnerabilities of every version - to compute
    /// three numbers from them.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="IContext"/>, which is what
    /// <see cref="PackageOwnershipService.GetOwnedPackageIdsAsync"/> already requires:
    /// this page cannot work on a database without one either way, so routing it through
    /// <see cref="IPackageDatabase"/> bought nothing.
    /// </remarks>
    internal static async Task<List<OwnedVersion>> ListVersionsAsync(
        IContext context,
        IReadOnlyList<string>? ids,
        CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        var lowered = ids.Select(id => id.ToLowerInvariant()).ToList();
        #pragma warning restore CS8604

        // Unlisted versions included: they are the owner's own, and an ID whose versions
        // are all unlisted would otherwise look empty.
        #pragma warning disable CS8602, CS8604
        return await context.Packages
            .AsNoTracking()
            .Where(p => lowered.Contains(p.Id.ToLower()))
            .Select(p => new OwnedVersion
            {
                Id = p.Id,
                NormalizedVersion = p.NormalizedVersionString,
                OriginalVersion = p.OriginalVersionString,
                Listed = p.Listed,
                Downloads = p.Downloads,
            })
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604
    }

    // A Maven repository keeps its own tables too: the owned ids are group:artifact, summarised one artifact at a time.
    internal static async Task<List<OwnedPackage>> SummariseMavenAsync(IMavenRepository maven, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var owned = new List<OwnedPackage>(ids.Count);
        foreach (var id in ids)
        {
            var found = MavenNames.TryParseArtifactName(id, out var groupId, out var artifactId);
            var artifact = found ? await maven.GetArtifactAsync(groupId, artifactId, cancellationToken) : null;
            owned.Add(new OwnedPackage
            {
                Id = artifact?.Name ?? id,
                LatestVersion = artifact?.Latest?.Version,
                LatestVersionIsListed = artifact != null,
                VersionCount = artifact?.Versions.Count ?? 0,
                Downloads = 0,
            });
        }

        return owned;
    }

    // An npm repository keeps its own tables: the summary comes from the registry, one package at a time (a user owns few).
    internal static async Task<List<OwnedPackage>> SummariseNpmAsync(INpmRegistry registry, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var owned = new List<OwnedPackage>(ids.Count);
        foreach (var id in ids)
        {
            var package = await registry.GetPackageAsync(id.ToLowerInvariant(), cancellationToken);
            owned.Add(new OwnedPackage
            {
                Id = package?.Name ?? id,
                LatestVersion = package?.Latest?.Version,
                LatestVersionIsListed = package != null,
                VersionCount = package?.Versions.Count ?? 0,
                Downloads = 0,
            });
        }

        return owned;
    }

    /// <summary>
    /// The highest version is decided here rather than in the query: version ordering is
    /// SemVer's, which no database sorts correctly.
    /// </summary>
    internal static List<OwnedPackage> Summarise(IReadOnlyList<string>? ids, List<OwnedVersion> versions)
    {
        #pragma warning disable CS8621, CS8714
        var byId = versions
            .GroupBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        #pragma warning restore CS8621, CS8714

        #pragma warning disable CS8602
        var owned = new List<OwnedPackage>(ids.Count);
        #pragma warning restore CS8602
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var found)) found = new List<OwnedVersion>();

            #pragma warning disable CS8604
            var latest = found
                .OrderByDescending(v => NuGetVersion.Parse(v.OriginalVersion ?? v.NormalizedVersion))
                .FirstOrDefault();
            #pragma warning restore CS8604

            #pragma warning disable CS8604
            owned.Add(new OwnedPackage
            {
                Id = latest?.Id ?? id,
                LatestVersion = latest == null
                    ? null
                    : NuGetVersion.Parse(latest.OriginalVersion ?? latest.NormalizedVersion).ToNormalizedString(),
                LatestVersionIsListed = latest?.Listed ?? false,
                VersionCount = found.Count,
                Downloads = found.Sum(v => v.Downloads),
            });
            #pragma warning restore CS8604
        }

        return owned;
    }
}
