using System;
using System.Collections.Generic;
using NuGet.Versioning;

namespace BaGet.Core;

// See NuGetGallery's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/Package.cs
public class Package : IRepositoryScoped
{
    public int Key { get; set; }

    /// <summary>
    /// The owning repository; see <see cref="IRepositoryScoped"/>.
    /// </summary>
    public int RepositoryKey { get; set; }

    public string? Id { get; set; }

    public NuGetVersion Version
    {
        get
        {
            // Favor the original version string as it contains more information.
            // Packages uploaded with older versions of BaGet may not have the original version string.
            #pragma warning disable CS8604
            return NuGetVersion.Parse(
                OriginalVersionString != null
                    ? OriginalVersionString
                    : NormalizedVersionString);
            #pragma warning restore CS8604
        }

        set
        {
            NormalizedVersionString = value.ToNormalizedString().ToLowerInvariant();
            OriginalVersionString = value.OriginalVersion;
        }
    }

    public string[]? Authors { get; set; }
    public string? Description { get; set; }
    public long Downloads { get; set; }
    public bool HasReadme { get; set; }
    public bool HasEmbeddedIcon { get; set; }
    public bool IsPrerelease { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? Language { get; set; }
    public bool Listed { get; set; }
    public string? MinClientVersion { get; set; }

    /// <summary>
    /// The time at which the package was published, in UTC.
    /// </summary>
    /// <remarks>
    /// Always stored as UTC. The database returns a <see cref="DateTime"/> whose
    /// <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Unspecified"/>, which would
    /// otherwise be read as local time when converted to a <see cref="DateTimeOffset"/>,
    /// shifting the published timestamp by the server's UTC offset.
    /// </remarks>
    public DateTime Published
    {
        get => DateTime.SpecifyKind(_published, DateTimeKind.Utc);
        set => _published = value;
    }

    private DateTime _published;

    public bool RequireLicenseAcceptance { get; set; }
    public SemVerLevel SemVerLevel { get; set; }
    public string? Summary { get; set; }
    public string? Title { get; set; }

    public Uri? IconUrl { get; set; }
    public Uri? LicenseUrl { get; set; }
    public Uri? ProjectUrl { get; set; }

    public Uri? RepositoryUrl { get; set; }
    public string? RepositoryType { get; set; }

    public string[]? Tags { get; set; }

    /// <summary>
    /// Used for optimistic concurrency.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public List<PackageDependency>? Dependencies { get; set; }
    public List<PackageType>? PackageTypes { get; set; }
    public List<TargetFramework>? TargetFrameworks { get; set; }

    /// <summary>
    /// The deprecation of this version, or null if it is not deprecated.
    /// </summary>
    public PackageDeprecation? Deprecation { get; set; }

    /// <summary>
    /// The known vulnerabilities affecting this version.
    /// </summary>
    public List<PackageVulnerability>? Vulnerabilities { get; set; }

    public string? NormalizedVersionString { get; set; }
    public string? OriginalVersionString { get; set; }


    public string IconUrlString => IconUrl?.AbsoluteUri ?? string.Empty;
    public string LicenseUrlString => LicenseUrl?.AbsoluteUri ?? string.Empty;
    public string ProjectUrlString => ProjectUrl?.AbsoluteUri ?? string.Empty;
    public string RepositoryUrlString => RepositoryUrl?.AbsoluteUri ?? string.Empty;
}