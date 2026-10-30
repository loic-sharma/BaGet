using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>What the npm package management screen (<c>/npm-manage/{*id}</c>) shows; filled by the screen's workflow.</summary>
public sealed class NpmManageViewModel
{
    /// <summary>The package name from the address.</summary>
    public string? Id { get; internal set; }

    /// <summary>The package, or null before it is loaded.</summary>
    public NpmPackageInfo? Package { get; internal set; }

    /// <summary>The package's owners.</summary>
    public IReadOnlyList<PackageOwner> Owners { get; internal set; } = Array.Empty<PackageOwner>();

    /// <summary>The outcome of the last action.</summary>
    public string? Message { get; internal set; }

    /// <summary>Whether <see cref="Message"/> reports a refusal.</summary>
    public bool Failed { get; internal set; }

    /// <summary>Whether the signed-in user is an administrator.</summary>
    public bool IsAdministrator { get; internal set; }

    /// <summary>Whether the user manages the package only as an administrator (they are not an owner).</summary>
    public bool ActingAsAdministrator { get; internal set; }

    /// <summary>Whether the user has a password to confirm with.</summary>
    public bool HasPassword { get; internal set; } = true;

    /// <summary>For an account without a password: it signed in again with its provider a moment ago.</summary>
    public bool Reauthenticated { get; internal set; }

    /// <summary>Whether an external provider is available to sign in again with.</summary>
    public bool CanReauthenticate { get; internal set; }

    /// <summary>The address npm is configured with for this repository.</summary>
    public string RegistryUrl { get; internal set; } = string.Empty;

    /// <summary>How long after publishing a version its owner may unpublish it with Deploy alone.</summary>
    public TimeSpan UnpublishWindow { get; internal set; }
}
