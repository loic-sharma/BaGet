using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>What the Maven artifact management screen (<c>/maven-manage/{group}/{artifact}</c>) shows; filled by the screen's workflow.</summary>
public sealed class MavenManageViewModel
{
    /// <summary>The group id from the address.</summary>
    public string? Group { get; internal set; }

    /// <summary>The artifact id from the address.</summary>
    public string? Artifact { get; internal set; }

    /// <summary>The version whose files are listed (<c>?version=</c>), or null.</summary>
    public string? Version { get; internal set; }

    /// <summary>The artifact, or null before it is loaded (or after its last version went).</summary>
    public MavenArtifactInfo? Info { get; internal set; }

    /// <summary>The chosen version with its files, or null.</summary>
    public MavenVersionDetail? Selected { get; internal set; }

    /// <summary>The artifact's owners.</summary>
    public IReadOnlyList<PackageOwner> Owners { get; internal set; } = Array.Empty<PackageOwner>();

    /// <summary>The outcome of the last action.</summary>
    public string? Message { get; internal set; }

    /// <summary>Whether <see cref="Message"/> reports a refusal.</summary>
    public bool Failed { get; internal set; }

    /// <summary>Whether the signed-in user is an administrator.</summary>
    public bool IsAdministrator { get; internal set; }

    /// <summary>Whether the user manages the artifact only as an administrator (they are not an owner).</summary>
    public bool ActingAsAdministrator { get; internal set; }

    /// <summary>Whether the user has a password to confirm with.</summary>
    public bool HasPassword { get; internal set; } = true;

    /// <summary>For an account without a password: it signed in again with its provider a moment ago.</summary>
    public bool Reauthenticated { get; internal set; }

    /// <summary>Whether an external provider is available to sign in again with.</summary>
    public bool CanReauthenticate { get; internal set; }

    /// <summary><c>group:artifact</c>.</summary>
    public string Name => MavenNames.ArtifactName(Group ?? string.Empty, Artifact ?? string.Empty);
}
