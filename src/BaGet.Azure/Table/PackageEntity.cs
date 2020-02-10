using System;
using BaGet.Core;
using Microsoft.Azure.Cosmos.Table;

namespace BaGet.Azure
{
    /// <summary>
    /// The Azure Table Storage entity that maps to a <see cref="Package"/>.
    /// The <see cref="TableEntity.PartitionKey"/> is the <see cref="Package.Id"/> and
    /// the <see cref="TableEntity.RowKey"/> is the <see cref="Package.Version"/>.
    /// </summary>
    public class PackageEntity : TableEntity, IDownloadCount, IListed
    {
        public PackageEntity()
        {
        }

        public string Id { get; set; }
        public string NormalizedVersion { get; set; }
        public string OriginalVersion { get; set; }
        public string Authors { get; set; }
        public string Description { get; set; }
        public long Downloads { get; set; }
        public bool HasReadme { get; set; }
        public bool HasEmbeddedIcon { get; set; }
        public bool IsPrerelease { get; set; }
        public string Language { get; set; }
        public bool Listed { get; set; }
        public string MinClientVersion { get; set; }
        public DateTime Published { get; set; }
        public bool RequireLicenseAcceptance { get; set; }
        public int SemVerLevel { get; set; }
        public string ReleaseNotes { get; set; }
        public string Summary { get; set; }
        public string Title { get; set; }

        public string IconUrl { get; set; }
        public string LicenseUrl { get; set; }
        public string ProjectUrl { get; set; }

        public string RepositoryUrl { get; set; }
        public string RepositoryType { get; set; }

        public string Tags { get; set; }
        public string Dependencies { get; set; }
        public string PackageTypes { get; set; }
        public string TargetFrameworks { get; set; }
    }

    /// <summary>
    /// A single item in <see cref="PackageEntity.Dependencies"/>.
    /// </summary>
    public class DependencyModel
    {
        public string Id { get; set; }
        public string VersionRange { get; set; }
        public string TargetFramework { get; set; }
    }

    /// <summary>
    /// A single item in <see cref="PackageEntity.PackageTypes"/>.
    /// </summary>
    public class PackageTypeModel
    {
        public string Name { get; set; }
        public string Version { get; set; }
    }

    /// <summary>
    /// The Azure Table Storage entity to update the <see cref="Package.Listed"/> column.
    /// The <see cref="TableEntity.PartitionKey"/> is the <see cref="Package.Id"/> and
    /// the <see cref="TableEntity.RowKey"/> is the <see cref="Package.Version"/>.
    /// </summary>
    public class PackageListingEntity : TableEntity, IListed
    {
        public PackageListingEntity()
        {
        }

        public bool Listed { get; set; }
    }

    /// <summary>
    /// The Azure Table Storage entity to update the <see cref="Package.Downloads"/> column.
    /// The <see cref="TableEntity.PartitionKey"/> is the <see cref="Package.Id"/> and
    /// the <see cref="TableEntity.RowKey"/> is the <see cref="Package.Version"/>.
    /// </summary>
    public class PackageDownloadsEntity : TableEntity, IDownloadCount
    {
        public PackageDownloadsEntity()
        {
        }

        public long Downloads { get; set; }
    }

    internal interface IListed
    {
        bool Listed { get; set; }
    }

    public interface IDownloadCount
    {
        long Downloads { get; set; }
    }
}
