using System;

namespace BaGet.Core;

/// <summary>
/// One entry in the append-only catalog log: a package version was added or changed,
/// or was deleted.
/// See: https://learn.microsoft.com/en-us/nuget/api/catalog-resource
/// </summary>
/// <remarks>
/// There is deliberately no foreign key to <see cref="Package"/>: the log outlives the
/// packages it records, and a delete entry refers to a package that no longer exists.
/// </remarks>
public class CatalogEntry : IRepositoryScoped
{
    public const string PackageDetailsType = "PackageDetails";
    public const string PackageDeleteType = "PackageDelete";

    public int Key { get; set; }

    /// <summary>
    /// The owning repository; see <see cref="IRepositoryScoped"/>.
    /// </summary>
    public int RepositoryKey { get; set; }

    /// <summary>
    /// The commit's unique ID. It also names the leaf document in storage.
    /// </summary>
    public string? CommitId { get; set; }

    /// <summary>
    /// When the entry was committed, in UTC. Strictly increasing in commit order.
    /// </summary>
    public DateTime CommitTimestamp { get; set; }

    /// <summary>
    /// <see cref="PackageDetailsType"/> or <see cref="PackageDeleteType"/>.
    /// </summary>
    public string? Type { get; set; }

    public string? PackageId { get; set; }

    /// <summary>
    /// The normalized version.
    /// </summary>
    public string? PackageVersion { get; set; }
}