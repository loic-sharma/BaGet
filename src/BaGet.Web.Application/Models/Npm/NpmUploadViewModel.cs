using System;
using System.Collections.Generic;

namespace BaGet.Web.Application.Models.Npm;

/// <summary>What the npm upload page (<c>/npm-upload</c>) shows, before and after a publish.</summary>
public sealed class NpmUploadViewModel
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

    /// <summary>The address npm is configured with for this repository.</summary>
    public string RegistryUrl { get; init; } = string.Empty;

    public IReadOnlyList<string> PublishLines { get; init; } = Array.Empty<string>();

    /// <summary>What happened, for the page to show.</summary>
    public string? Message { get; init; }

    /// <summary>Whether <see cref="Message"/> reports a failure.</summary>
    public bool Failed { get; init; }

    /// <summary>The package just published, so the page can link to it.</summary>
    public string? PublishedName { get; init; }

    /// <summary>The HTTP status the page is answered with (200, or the refusal's).</summary>
    public int StatusCode { get; init; } = 200;
}
