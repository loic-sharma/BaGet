using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;
using Microsoft.Azure.Cosmos.Table;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Azure
{
    public class TableOperationBuilder
    {
        public TableOperation AddPackage(Package package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var version = package.Version;
            var normalizedVersion = version.ToNormalizedString();

            var entity = new PackageEntity
            {
                PartitionKey = package.Id.ToLowerInvariant(),
                RowKey = normalizedVersion.ToLowerInvariant(),

                Id = package.Id,
                NormalizedVersion = normalizedVersion,
                OriginalVersion = version.ToFullString(),
                Authors = JsonConvert.SerializeObject(package.Authors),
                Description = package.Description,
                Downloads = package.Downloads,
                HasReadme = package.HasReadme,
                HasEmbeddedIcon = package.HasEmbeddedIcon,
                IsPrerelease = package.IsPrerelease,
                Language = package.Language,
                Listed = package.Listed,
                MinClientVersion = package.MinClientVersion,
                Published = package.Published,
                RequireLicenseAcceptance = package.RequireLicenseAcceptance,
                SemVerLevel = (int)package.SemVerLevel,
                Summary = package.Summary,
                Title = package.Title,
                IconUrl = package.IconUrlString,
                LicenseUrl = package.LicenseUrlString,
                ReleaseNotes = package.ReleaseNotes,
                ProjectUrl = package.ProjectUrlString,
                RepositoryUrl = package.RepositoryUrlString,
                RepositoryType = package.RepositoryType,
                Tags = JsonConvert.SerializeObject(package.Tags),
                Dependencies = SerializeList(package.Dependencies, AsDependencyModel),
                PackageTypes = SerializeList(package.PackageTypes, AsPackageTypeModel),
                TargetFrameworks = SerializeList(package.TargetFrameworks, f => f.Moniker)
            };

            return TableOperation.Insert(entity);
        }

        public TableOperation UpdateDownloads(string packageId, NuGetVersion packageVersion, long downloads)
        {
            var entity = new PackageDownloadsEntity();

            entity.PartitionKey = packageId.ToLowerInvariant();
            entity.RowKey = packageVersion.ToNormalizedString().ToLowerInvariant();
            entity.Downloads = downloads;
            entity.ETag = "*";

            return TableOperation.Merge(entity);
        }

        public TableOperation HardDeletePackage(string packageId, NuGetVersion packageVersion)
        {
            var entity = new PackageEntity();

            entity.PartitionKey = packageId.ToLowerInvariant();
            entity.RowKey = packageVersion.ToNormalizedString().ToLowerInvariant();
            entity.ETag = "*";

            return TableOperation.Delete(entity);
        }

        public TableOperation UnlistPackage(string packageId, NuGetVersion packageVersion)
        {
            var entity = new PackageListingEntity();

            entity.PartitionKey = packageId.ToLowerInvariant();
            entity.RowKey = packageVersion.ToNormalizedString().ToLowerInvariant();
            entity.Listed = false;
            entity.ETag = "*";

            return TableOperation.Merge(entity);
        }

        public TableOperation RelistPackage(string packageId, NuGetVersion packageVersion)
        {
            var entity = new PackageListingEntity();

            entity.PartitionKey = packageId.ToLowerInvariant();
            entity.RowKey = packageVersion.ToNormalizedString().ToLowerInvariant();
            entity.Listed = true;
            entity.ETag = "*";

            return TableOperation.Merge(entity);
        }

        private static string SerializeList<TIn, TOut>(IReadOnlyList<TIn> objects, Func<TIn, TOut> map)
        {
            var data = objects.Select(map).ToList();

            return JsonConvert.SerializeObject(data);
        }

        public static DependencyModel AsDependencyModel(PackageDependency dependency)
        {
            return new DependencyModel
            {
                Id = dependency.Id,
                VersionRange = dependency.VersionRange,
                TargetFramework = dependency.TargetFramework
            };
        }

        public static PackageTypeModel AsPackageTypeModel(PackageType packageType)
        {
            return new PackageTypeModel
            {
                Name = packageType.Name,
                Version = packageType.Version
            };
        }
    }
}
