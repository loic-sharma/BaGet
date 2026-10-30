using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;

namespace BaGet.Web.V2.Models;

/// <summary>
/// A package as the V2 protocol describes it. The V2 entity carries a flatter set of
/// properties than <see cref="Package"/>, and some of them - "IsLatestVersion" above
/// all - only have an answer in the context of the other versions returned alongside.
/// </summary>
internal class V2FeedPackage
{
    public string? Id { get; set; }
    public string? Version { get; set; }
    public string? NormalizedVersion { get; set; }
    public string? Authors { get; set; }
    public string? Description { get; set; }
    public string? Summary { get; set; }
    public string? Title { get; set; }
    public string? Tags { get; set; }
    public string? Language { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? MinClientVersion { get; set; }
    public string? Dependencies { get; set; }
    public string? IconUrl { get; set; }
    public string? LicenseUrl { get; set; }
    public string? ProjectUrl { get; set; }
    public long DownloadCount { get; set; }
    public bool RequireLicenseAcceptance { get; set; }
    public bool Listed { get; set; }
    public bool IsPrerelease { get; set; }
    public bool IsLatestVersion { get; set; }
    public bool IsAbsoluteLatestVersion { get; set; }
    public DateTime Published { get; set; }
    public DateTime LastUpdated { get; set; }
    public string? DownloadUrl { get; set; }

    /// <summary>
    /// Project a set of versions of one package. The whole set is needed at once
    /// because the latest-version flags are relative to it.
    /// </summary>
    /// <param name="packages">Every version, whatever the client may see.</param>
    /// <param name="downloadUrl">The download URL of a version.</param>
    /// <param name="include">Which versions are returned; the flags are still computed over all of <paramref name="packages"/>.</param>
    public static List<V2FeedPackage> FromPackages(
        IEnumerable<Package> packages,
        Func<Package, string> downloadUrl,
        Func<Package, bool>? include = null)
    {
        var ordered = packages
            .OrderByDescending(p => p.Version)
            .ToList();

        // "IsAbsoluteLatestVersion" is the newest version of all; "IsLatestVersion"
        // is the newest stable one. An id with only prereleases has no latest version.
        var absoluteLatest = ordered.FirstOrDefault(p => p.Listed);
        var latest = ordered.FirstOrDefault(p => p.Listed && !p.IsPrerelease);

        return ordered
            .Where(p => include == null || include(p))
            .Select(p => FromPackage(p, downloadUrl(p), ReferenceEquals(p, latest), ReferenceEquals(p, absoluteLatest)))
            .ToList();
    }

    private static V2FeedPackage FromPackage(
        Package package,
        string downloadUrl,
        bool isLatestVersion,
        bool isAbsoluteLatestVersion)
    {
        return new V2FeedPackage
        {
            Id = package.Id,
            Version = package.OriginalVersionString ?? package.NormalizedVersionString,
            NormalizedVersion = package.NormalizedVersionString,
            Authors = string.Join(", ", package.Authors ?? Array.Empty<string>()),
            Description = package.Description ?? string.Empty,
            Summary = package.Summary ?? string.Empty,
            Title = package.Title ?? string.Empty,
            Tags = string.Join(" ", package.Tags ?? Array.Empty<string>()),
            Language = package.Language ?? string.Empty,
            ReleaseNotes = package.ReleaseNotes ?? string.Empty,
            MinClientVersion = package.MinClientVersion ?? string.Empty,
            Dependencies = FormatDependencies(package.Dependencies),
            IconUrl = package.IconUrlString ?? string.Empty,
            LicenseUrl = package.LicenseUrlString ?? string.Empty,
            ProjectUrl = package.ProjectUrlString ?? string.Empty,
            DownloadCount = package.Downloads,
            RequireLicenseAcceptance = package.RequireLicenseAcceptance,
            Listed = package.Listed,
            IsPrerelease = package.IsPrerelease,
            IsLatestVersion = isLatestVersion,
            IsAbsoluteLatestVersion = isAbsoluteLatestVersion,
            Published = package.Published,
            LastUpdated = package.Published,
            DownloadUrl = downloadUrl,
        };
    }

    /// <summary>
    /// V2 flattens dependencies into one string: groups separated by "|", each entry
    /// "id:range:framework". A group with no packages contributes "::framework".
    /// </summary>
    internal static string FormatDependencies(IEnumerable<PackageDependency>? dependencies)
    {
        if (dependencies == null) return string.Empty;

        return string.Join("|", dependencies.Select(d =>
            $"{d.Id}:{d.VersionRange}:{d.TargetFramework}"));
    }
}