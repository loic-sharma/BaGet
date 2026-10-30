using BaGet.Web.Application.UseCases;

namespace BaGet.Web.Application.Models.Upload;

/// <summary>What the NuGet upload page (<c>/upload</c>) shows: the form, the preview of a staged file, or the outcome of a step.</summary>
public sealed class UploadViewModel
{
    /// <summary>
    /// The package ID a link arrived with, as "upload a new version of this" does. A hint for the page only: the ID and version that
    /// are published are read from the file, never from the request.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>Whether accounts are on (the uploader then signs in).</summary>
    public bool AccountsEnabled { get; set; }

    /// <summary>Whether the server is read-only.</summary>
    public bool IsReadOnlyMode { get; set; }

    /// <summary>Whether the visitor is signed in.</summary>
    public bool SignedIn { get; set; }

    /// <summary>With accounts off there is no user to own an upload, so the pusher proves itself with an API key.</summary>
    public bool NeedsApiKey => !AccountsEnabled;

    /// <summary>
    /// Nothing on this server can tell one caller from another (accounts off and no global API key): the browser upload is closed.
    /// </summary>
    public bool NothingCanAuthenticate { get; set; }

    /// <summary>What the page says in that state, so the explanation and the refusal are one sentence.</summary>
    public string NoApiKeyExplanation => UploadUseCase.NoApiKeyMessage;

    /// <summary>Whether to render the form at all.</summary>
    public bool CanUpload => !IsReadOnlyMode && !NothingCanAuthenticate && (!AccountsEnabled || SignedIn);

    /// <summary>The staged upload awaiting confirmation, or null while on step one.</summary>
    public UploadPreview? Staged { get; set; }

    /// <summary>What happened, for the page to show.</summary>
    public string? Message { get; set; }

    /// <summary>Whether <see cref="Message"/> reports a failure.</summary>
    public bool Failed { get; set; }

    /// <summary>Set once something is published, so the page can link to it.</summary>
    public UploadPreview? Published { get; set; }

    /// <summary>The token of a staged upload whose API key was rejected, so the key can be entered again.</summary>
    public string? RetryToken { get; set; }

    /// <summary>The HTTP status the page is answered with (200, or the refusal's).</summary>
    public int StatusCode { get; set; } = 200;
}
