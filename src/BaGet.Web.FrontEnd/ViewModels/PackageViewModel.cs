using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;
using BaGet.Web.V3.Controllers.UI;
using Microsoft.AspNetCore.Html;
using NuGet.Versioning;
using BaGet.Web.Application.Models.Packages;
using BaGet.Web.Application.Screens;
using BaGet.Web.Application.Snippets;

namespace BaGet.Web.V3.ViewModels;

/// <summary>What the package page shows; filled from the screen the JSON twin answered (<see cref="PackageApiController"/>).</summary>
public sealed class PackageViewModel
{
    /// <summary>False when the package (or any listed version of it) does not exist; <see cref="Package"/> then only names the ID.</summary>
    public bool Found { get; internal set; }

    /// <summary>The version shown.</summary>
    public Package? Package { get; internal set; }

    /// <summary>The package is a dotnet template.</summary>
    public bool IsDotnetTemplate { get; internal set; }

    /// <summary>The package is a dotnet tool.</summary>
    public bool IsDotnetTool { get; internal set; }

    /// <summary>When any version was last published.</summary>
    public DateTime LastUpdated { get; internal set; }

    /// <summary>Downloads of every version.</summary>
    public long TotalDownloads { get; internal set; }

    /// <summary>The packages that depend on this one.</summary>
    public IReadOnlyList<PackageDependent>? UsedBy { get; internal set; }

    /// <summary>The dependencies, by framework.</summary>
    public IReadOnlyList<DependencyGroupModel>? DependencyGroups { get; internal set; }

    /// <summary>The listed versions, newest first.</summary>
    public IReadOnlyList<PackageVersionRow>? Versions { get; internal set; }

    /// <summary>The readme, rendered safely.</summary>
    public HtmlString? Readme { get; internal set; }

    /// <summary>The frameworks the package supports, by family.</summary>
    internal IReadOnlyList<FrameworkGroup> FrameworkGroups { get; set; } = Array.Empty<FrameworkGroup>();
    /// <summary>The install commands, one tab per tool.</summary>
    internal IReadOnlyList<InstallSnippet> InstallTabs { get; set; } = Array.Empty<InstallSnippet>();

    /// <summary>Owner names; empty unless accounts are enabled and the package has owners.</summary>
    public IReadOnlyList<string?> Owners { get; internal set; } = Array.Empty<string>();

    /// <summary>The avatar address of each owner by name; empty when Gravatar is off or accounts are disabled.</summary>
    public IReadOnlyDictionary<string, string> OwnerAvatars { get; internal set; } = new Dictionary<string, string>();

    /// <summary>
    /// The address of the management page when the signed in user may open it (an owner, directly or through an organization, or an
    /// administrator), else null and the page offers no link.
    /// </summary>
    public string? ManageUrl { get; internal set; }

    /// <summary>The icon address.</summary>
    public string? IconUrl { get; internal set; }

    /// <summary>The license address, null for a license expression or file.</summary>
    public string? LicenseUrl { get; internal set; }

    /// <summary>Where the package file is downloaded.</summary>
    public string? PackageDownloadUrl { get; internal set; }

    /// <summary>
    /// This version on nuget.info, the web app of NuGet Package Explorer, as nuget.org links it. nuget.info always downloads from
    /// nuget.org, so the link only works for a package that is also published there.
    /// </summary>
    public string? PackageExplorerUrl { get; internal set; }

    /// <summary>The model of the view for what the JSON twin answered. The package is a flat copy: only the members the page prints are set.</summary>
    internal static PackageViewModel From(PackageScreen screen)
    {
        var details = screen.Package;
        return new PackageViewModel
        {
            Found = screen.Found,
            Package = new Package
            {
                Id = details.Id,
                NormalizedVersionString = details.Version,
                Description = details.Description,
                ReleaseNotes = details.ReleaseNotes,
                ProjectUrl = Uri.TryCreate(details.ProjectUrl, UriKind.Absolute, out var project) ? project : null,
                RepositoryUrl = Uri.TryCreate(details.RepositoryUrl, UriKind.Absolute, out var source) ? source : null,
                Downloads = details.Downloads,
                Authors = details.Authors.ToArray(),
            },
            IsDotnetTemplate = screen.IsDotnetTemplate,
            IsDotnetTool = screen.IsDotnetTool,
            LastUpdated = screen.LastUpdated,
            TotalDownloads = screen.TotalDownloads,
            UsedBy = screen.UsedBy,
            DependencyGroups = screen.DependencyGroups,
            Versions = screen.Versions
                .Select(v => new PackageVersionRow { Version = NuGetVersion.Parse(v.Version), Downloads = v.Downloads, Selected = v.Selected, LastUpdated = v.LastUpdated })
                .ToList(),
            Readme = screen.ReadmeHtml == null ? null : new HtmlString(screen.ReadmeHtml),
            FrameworkGroups = screen.FrameworkGroups,
            InstallTabs = screen.InstallTabs,
            Owners = screen.Owners,
            OwnerAvatars = screen.OwnerAvatars,
            ManageUrl = screen.ManageUrl,
            IconUrl = screen.IconUrl,
            LicenseUrl = screen.LicenseUrl,
            PackageDownloadUrl = screen.PackageDownloadUrl,
            PackageExplorerUrl = screen.PackageExplorerUrl,
        };
    }
}
