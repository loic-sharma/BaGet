using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core.Entities;
using Microsoft.Azure.Cosmos.Table;
using Newtonsoft.Json;

namespace BaGet.Azure
{
    public partial class TablePackageService
    {
        /// <summary>
        /// The Azure Table Storage entity that maps to a <see cref="Package"/>.
        /// The <see cref="TableEntity.PartitionKey"/> is the <see cref="Package.Id"/> and
        /// the <see cref="TableEntity.RowKey"/> is the <see cref="Package.Version"/>.
        /// </summary>
        private class PackageEntity : TableEntity
        {
            public PackageEntity()
            {
            }

            public static PackageEntity FromPackage(Package package)
            {
                if (package == null) throw new ArgumentNullException(nameof(package));

                return new PackageEntity
                {
                    PartitionKey = package.Id.ToLowerInvariant(),
                    RowKey = package.Version.ToNormalizedString().ToLowerInvariant(),

                    Id = package.Id,
                    FullVersion = package.Version.ToFullString(),
                    Authors = JsonConvert.SerializeObject(package.Authors),
                    Description = package.Description,
                    Downloads = package.Downloads,
                    HasReadme = package.HasReadme,
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
                    ProjectUrl = package.ProjectUrlString,
                    RepositoryUrl = package.RepositoryUrlString,
                    RepositoryType = package.RepositoryType,
                    Tags = JsonConvert.SerializeObject(package.Tags),
                    Dependencies = SerializeList(package.Dependencies, DependencyEntity.FromPackageDependency),
                    PackageTypes = SerializeList(package.PackageTypes, PackageTypeEntity.FromPackageType),
                    TargetFrameworks = SerializeList(package.TargetFrameworks, f => f.Moniker)
                };
            }

            private static string SerializeList<TIn, TOut>(IReadOnlyList<TIn> objects, Func<TIn, TOut> map)
            {
                var data = objects.Select(map).ToList();

                return JsonConvert.SerializeObject(data);
            }

            public string Id { get; set; }
            public string FullVersion { get; set; }
            public string Authors { get; set; }
            public string Description { get; set; }
            public long Downloads { get; set; }
            public bool HasReadme { get; set; }
            public bool IsPrerelease { get; set; }
            public string Language { get; set; }
            public bool Listed { get; set; }
            public string MinClientVersion { get; set; }
            public DateTime Published { get; set; }
            public bool RequireLicenseAcceptance { get; set; }
            public int SemVerLevel { get; set; }
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

            public Package AsPackage()
            {
                var targetFrameworks = JsonConvert.DeserializeObject<List<string>>(TargetFrameworks)
                    .Select(f => new TargetFramework { Moniker = f })
                    .ToList();

                return new Package
                {
                    Id = Id,
                    VersionString = FullVersion,

                    Authors = JsonConvert.DeserializeObject<string[]>(Authors),
                    Description = Description,
                    Downloads = Downloads,
                    HasReadme = HasReadme,
                    IsPrerelease = IsPrerelease,
                    Language = Language,
                    Listed = Listed,
                    MinClientVersion = MinClientVersion,
                    Published = Published,
                    RequireLicenseAcceptance = RequireLicenseAcceptance,
                    SemVerLevel = (SemVerLevel)SemVerLevel,
                    Summary = Summary,
                    Title = Title,
                    IconUrl = new Uri(IconUrl),
                    LicenseUrl = new Uri(LicenseUrl),
                    ProjectUrl = new Uri(ProjectUrl),
                    RepositoryUrl = new Uri(RepositoryUrl),
                    RepositoryType = RepositoryType,
                    Tags = JsonConvert.DeserializeObject<string[]>(Tags),
                    Dependencies = DependencyEntity.Parse(Dependencies),
                    PackageTypes = PackageTypeEntity.Parse(PackageTypes),
                    TargetFrameworks = targetFrameworks,
                };
            }

            private class DependencyEntity
            {
                public static DependencyEntity FromPackageDependency(PackageDependency dependency)
                {
                    return new DependencyEntity
                    {
                        Id = dependency.Id,
                        VersionRange = dependency.VersionRange,
                        TargetFramework = dependency.TargetFramework
                    };
                }

                public static List<PackageDependency> Parse(string json)
                {
                    return JsonConvert.DeserializeObject<List<DependencyEntity>>(json)
                        .Select(e => new PackageDependency
                        {
                            Id = e.Id,
                            VersionRange = e.VersionRange,
                            TargetFramework = e.TargetFramework,
                        })
                        .ToList();
                }

                public string Id { get; set; }
                public string VersionRange { get; set; }
                public string TargetFramework { get; set; }
            }

            private class PackageTypeEntity
            {
                public static PackageTypeEntity FromPackageType(PackageType packageType)
                {
                    return new PackageTypeEntity
                    {
                        Name = packageType.Name,
                        Version = packageType.Version
                    };
                }

                public static List<PackageType> Parse(string json)
                {
                    return JsonConvert.DeserializeObject<List<PackageTypeEntity>>(json)
                        .Select(e => new PackageType
                        {
                            Name = e.Name,
                            Version = e.Version
                        })
                        .ToList();
                }

                public string Name { get; set; }
                public string Version { get; set; }
            }
        }
    }
}
