using System;

namespace BaGet.Core;

/// <summary>
/// A report that a package version is abusive, for its owners and the server's
/// administrator to act on.
/// </summary>
public class AbuseReport : IRepositoryScoped
{
    /// <summary>
    /// The reasons a report may give, as nuget.org offers them.
    /// </summary>
    public static readonly string[] Reasons =
    {
        "Contains malicious code",
        "Violates a license or copyright",
        "Contains private or confidential data",
        "Other",
    };

    public int Key { get; set; }

    /// <summary>
    /// The owning repository; see <see cref="IRepositoryScoped"/>.
    /// </summary>
    public int RepositoryKey { get; set; }

    public string? PackageId { get; set; }

    public string? PackageVersion { get; set; }

    public string? Reason { get; set; }

    public string? Details { get; set; }

    /// <summary>
    /// Where to reply, if the reporter gave an address.
    /// </summary>
    public string? ReporterEmail { get; set; }

    public DateTime Created { get; set; }

    public DateTime? Resolved { get; set; }
}