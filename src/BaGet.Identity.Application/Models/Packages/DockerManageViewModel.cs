using BaGet.Core;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>What the Docker image management screen (<c>/docker-manage/{**image}</c>) shows; filled by the screen's workflow.</summary>
public sealed class DockerManageViewModel
{
    /// <summary>The image name from the address (it may contain slashes).</summary>
    public string? Image { get; internal set; }

    /// <summary>The image, or null before it is loaded (or after it was deleted).</summary>
    public OciImageInfo? Info { get; internal set; }

    /// <summary>The outcome of the last action.</summary>
    public string? Message { get; internal set; }

    /// <summary>Whether <see cref="Message"/> reports a refusal.</summary>
    public bool Failed { get; internal set; }

    /// <summary>Whether the user has a password to confirm with.</summary>
    public bool HasPassword { get; internal set; } = true;

    /// <summary>For an account without a password: it signed in again with its provider a moment ago.</summary>
    public bool Reauthenticated { get; internal set; }

    /// <summary>Whether an external provider is available to sign in again with.</summary>
    public bool CanReauthenticate { get; internal set; }
}
