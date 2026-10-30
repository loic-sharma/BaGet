using System;
using System.Collections.Generic;
using BaGet.Core;
using Microsoft.AspNetCore.Html;
using BaGet.Web.Application.Snippets;

namespace BaGet.Web.Application.Models.Npm;

/// <summary>What the page of one npm package (<c>/npm-packages/{name}</c>) shows.</summary>
public sealed class NpmPackageViewModel
{
    /// <summary>The package name from the address.</summary>
    public string? Id { get; init; }

    /// <summary>The version asked for with <c>?version=</c>; the latest when absent or unknown.</summary>
    public string? RequestedVersion { get; init; }

    /// <summary>The HTTP status the page is answered with: 200, or 404 when the package does not exist (the page then explains).</summary>
    public int StatusCode { get; init; } = 200;

    /// <summary>The package, or null when there is none of that name.</summary>
    public NpmPackageInfo? Package { get; init; }

    /// <summary>The version the page shows.</summary>
    public NpmVersionInfo? Selected { get; init; }

    /// <summary>The selected version's readme as safe HTML, or null.</summary>
    public HtmlString? Readme { get; init; }

    /// <summary>The dist-tags that point at the selected version.</summary>
    public IReadOnlyList<string> SelectedTags { get; init; } = Array.Empty<string>();

    /// <summary>The address npm is configured with for this repository.</summary>
    public string RegistryUrl { get; init; } = string.Empty;

    /// <summary>Where the selected version's tarball is.</summary>
    public string? TarballUrl => Selected == null || Package == null
        ? null
        : RegistryUrl + Package.Name + "/-/" + NpmNames.TarballFileName(Package.Name, Selected.Version);

    public IReadOnlyList<InstallSnippet> InstallTabs { get; init; } = Array.Empty<InstallSnippet>();

    /// <summary>The owners, when accounts are on; empty otherwise.</summary>
    public IReadOnlyList<string> Owners { get; init; } = Array.Empty<string>();

    /// <summary>Whether the signed-in user can open the manage page (an owner or an administrator).</summary>
    public bool CanManage { get; init; }
}
