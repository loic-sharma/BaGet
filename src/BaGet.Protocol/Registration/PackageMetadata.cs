﻿using System;
using System.Collections.Generic;
using BaGet.Protocol.Converters;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    public class PackageMetadata
    {
        public PackageMetadata(
            string catalogUri,
            string packageId,
            NuGetVersion version,
            string authors,
            string description,
            long downloads,
            bool hasReadme,
            string iconUrl,
            string language,
            string licenseUrl,
            bool listed,
            string minClientVersion,
            string packageContent,
            IReadOnlyList<string> packageTypes,
            string projectUrl,
            string repositoryUrl,
            string repositoryType,
            DateTime published,
            bool requireLicenseAcceptance,
            string summary,
            IReadOnlyList<string> tags,
            string title,
            IReadOnlyList<DependencyGroupItem> dependencyGroups)
        {
            CatalogUri = catalogUri ?? throw new ArgumentNullException(nameof(catalogUri));

            PackageId = packageId;
            Version = version;
            Authors = authors;
            Description = description;
            Downloads = downloads;
            HasReadme = hasReadme;
            IconUrl = iconUrl;
            Language = language;
            LicenseUrl = licenseUrl;
            Listed = listed;
            MinClientVersion = minClientVersion;
            PackageContent = packageContent;
            PackageTypes = packageTypes;
            ProjectUrl = projectUrl;
            RepositoryUrl = repositoryUrl;
            RepositoryType = repositoryType;
            Published = published;
            RequireLicenseAcceptance = requireLicenseAcceptance;
            Summary = summary;
            Tags = tags;
            Title = title;
            DependencyGroups = dependencyGroups;
        }

        [JsonProperty(PropertyName = "@id")]
        public string CatalogUri { get; }

        [JsonProperty(PropertyName = "id")]
        public string PackageId { get; }

        [JsonConverter(typeof(NuGetVersionConverter))]
        public NuGetVersion Version { get; }

        public string Authors { get; }
        public string Description { get; }
        public long Downloads { get; }
        public bool HasReadme { get; }
        public string IconUrl { get; }
        public string Language { get; }
        public string LicenseUrl { get; }
        public bool Listed { get; }
        public string MinClientVersion { get; }
        public string PackageContent { get; }
        public IReadOnlyList<string> PackageTypes { get; }
        public string ProjectUrl { get; }
        public string RepositoryUrl { get; }
        public string RepositoryType { get; }
        public DateTime Published { get; }
        public bool RequireLicenseAcceptance { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Tags { get; }
        public string Title { get; }
        public IReadOnlyList<DependencyGroupItem> DependencyGroups { get; }
    }

    public class DependencyGroupItem
    {
        public DependencyGroupItem(
            string id,
            string targetFramework,
            IReadOnlyList<DependencyItem> dependencies)
        {
            Id = id;
            Type = "PackageDependencyGroup";
            TargetFramework = targetFramework;
            Dependencies = (dependencies?.Count > 0) ? dependencies : null;
        }

        [JsonProperty(PropertyName = "@id")]
        public string Id { get; }

        [JsonProperty(PropertyName = "@type")]
        public string Type { get; }

        public string TargetFramework { get; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public IReadOnlyList<DependencyItem> Dependencies { get; }
    }

    public class DependencyItem
    {
        [JsonProperty(PropertyName = "@id")]
        public string DepId { get; }

        [JsonProperty(PropertyName = "@type")]
        public string Type { get; }

        public string Id { get; }
        public string Range { get; }

        public DependencyItem(string depId, string id, string range)
        {
            DepId = depId;
            Type = "PackageDependency";
            Id = id;
            Range = range;
        }
    }
}
