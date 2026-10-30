using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;

namespace BaGet.Azure;

public static class PackageEntityExtensions
{
    public static Package AsPackage(this PackageEntity entity)
    {
        return new Package
        {
            Id = entity.Id,
            NormalizedVersionString = entity.NormalizedVersion,
            OriginalVersionString = entity.OriginalVersion,

            Authors = TableJson.Deserialize<string[]>(entity.Authors),
            Description = entity.Description,
            Downloads = entity.Downloads,
            HasReadme = entity.HasReadme,
            HasEmbeddedIcon = entity.HasEmbeddedIcon,
            IsPrerelease = entity.IsPrerelease,
            Language = entity.Language,
            Listed = entity.Listed,
            MinClientVersion = entity.MinClientVersion,
            Published = entity.Published,
            RequireLicenseAcceptance = entity.RequireLicenseAcceptance,
            SemVerLevel = (SemVerLevel)entity.SemVerLevel,
            Summary = entity.Summary,
            Title = entity.Title,
            ReleaseNotes = entity.ReleaseNotes,
            IconUrl = ParseUri(entity.IconUrl),
            LicenseUrl = ParseUri(entity.LicenseUrl),
            ProjectUrl = ParseUri(entity.ProjectUrl),
            RepositoryUrl = ParseUri(entity.RepositoryUrl),
            RepositoryType = entity.RepositoryType,
            Tags = TableJson.Deserialize<string[]>(entity.Tags),
            Dependencies = ParseDependencies(entity.Dependencies),
            PackageTypes = ParsePackageTypes(entity.PackageTypes),
            TargetFrameworks = ParseTargetFrameworks(entity.TargetFrameworks),
        };
    }

    private static Uri? ParseUri(string? input)
    {
        return string.IsNullOrEmpty(input) ? null : new Uri(input);
    }

    private static List<PackageDependency> ParseDependencies(string? input)
    {
        return TableJson.Deserialize<List<DependencyModel>>(input)
            .Select(e => new PackageDependency
            {
                Id = e.Id,
                VersionRange = e.VersionRange,
                TargetFramework = e.TargetFramework,
            })
            .ToList();
    }

    private static List<PackageType> ParsePackageTypes(string? input)
    {
        return TableJson.Deserialize<List<PackageTypeModel>>(input)
            .Select(e => new PackageType
            {
                Name = e.Name,
                Version = e.Version
            })
            .ToList();
    }

    private static List<TargetFramework> ParseTargetFrameworks(string? targetFrameworks)
    {
        return TableJson.Deserialize<List<string>>(targetFrameworks)
            .Select(f => new TargetFramework { Moniker = f })
            .ToList();
    }
}