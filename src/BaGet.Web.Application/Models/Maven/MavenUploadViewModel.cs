using BaGet.Core;

namespace BaGet.Web.Application.Models.Maven;

/// <summary>What the Maven upload page (<c>/maven-upload</c>) shows, before and after a deployment.</summary>
public sealed class MavenUploadViewModel
{
    /// <summary>Whether accounts are on (the uploader then signs in).</summary>
    public bool AccountsEnabled { get; init; }

    /// <summary>Whether the server is read-only.</summary>
    public bool IsReadOnlyMode { get; init; }

    /// <summary>Whether the visitor is signed in.</summary>
    public bool SignedIn { get; init; }

    /// <summary>Without accounts there is no user to own an upload, so the uploader proves itself with the API key.</summary>
    public bool NeedsApiKey => !AccountsEnabled;

    /// <summary>Accounts are off and no API key is set: nothing tells one visitor from another, so the page is closed.</summary>
    public bool NothingCanAuthenticate { get; init; }

    /// <summary>Whether to render the form: not read-only, someone could be authenticated, and (with accounts) signed in.</summary>
    public bool CanUpload => !IsReadOnlyMode && !NothingCanAuthenticate && (!AccountsEnabled || SignedIn);

    /// <summary>The address Maven is configured with for this repository.</summary>
    public string RepositoryUrl { get; init; } = string.Empty;

    /// <summary>The repository's name (the <c>id</c> in <c>settings.xml</c>).</summary>
    public string RepositoryName { get; init; } = string.Empty;

    /// <summary>The repository's policy, shown beside the form.</summary>
    public MavenRepositorySettings Settings { get; init; } = new();

    /// <summary>What happened, for the page to show.</summary>
    public string? Message { get; init; }

    /// <summary>Whether <see cref="Message"/> reports a failure.</summary>
    public bool Failed { get; init; }

    /// <summary>The artifact just deployed (<c>group:artifact</c>), so the page can link to it.</summary>
    public string? DeployedName { get; init; }

    /// <summary>The version just deployed.</summary>
    public string? DeployedVersion { get; init; }

    /// <summary>The coordinates just posted, so a refused form keeps them.</summary>
    public MavenDeployment? Posted { get; init; }

    /// <summary>The HTTP status the page is answered with (200, or the refusal's).</summary>
    public int StatusCode { get; init; } = 200;
}
