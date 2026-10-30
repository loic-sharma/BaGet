using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Html;
using BaGet.Web.Application.Models.Packages;

namespace BaGet.Web.Application.Models.Upload;

/// <summary>What a staged NuGet upload contains, as the preview of the upload page shows it (read from the archive itself).</summary>
public sealed class UploadPreview
{
    /// <summary>The handle of the staged file; only the user who staged it can open it.</summary>
    public string? Token { get; set; }

    /// <summary>Whether the file is a symbol package (<c>.snupkg</c>, by its nuspec's package type).</summary>
    public bool IsSymbolPackage { get; set; }

    /// <summary>The package ID, from the nuspec.</summary>
    public string? Id { get; set; }

    /// <summary>The normalized version, from the nuspec.</summary>
    public string? Version { get; set; }

    /// <summary>The description.</summary>
    public string? Description { get; set; }

    /// <summary>The authors.</summary>
    public IReadOnlyList<string> Authors { get; set; } = Array.Empty<string>();

    /// <summary>The license expression or license file the package declares, if any.</summary>
    public string? License { get; set; }

    /// <summary>The license URL, if any.</summary>
    public string? LicenseUrl { get; set; }

    /// <summary>The dependencies by target framework.</summary>
    public IReadOnlyList<DependencyGroupModel> DependencyGroups { get; set; } = Array.Empty<DependencyGroupModel>();

    /// <summary>The readme as safe HTML, if the package has one.</summary>
    public HtmlString? Readme { get; set; }

    /// <summary>The PDBs of a symbol package.</summary>
    public IReadOnlyList<string> SymbolFiles { get; set; } = Array.Empty<string>();

    /// <summary>Whether the version a symbol package belongs to is on this server.</summary>
    public bool TargetPackageExists { get; set; }

    /// <summary>What to tell about a symbol package whose package is missing.</summary>
    public string? TargetPackageMessage { get; set; }
}
