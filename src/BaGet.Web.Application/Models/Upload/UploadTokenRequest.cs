
namespace BaGet.Web.Application.Models.Upload;

/// <summary>
/// What the second and third step of the NuGet upload send (publish and discard a staged upload): the handle of the staged file and, while accounts are
/// off, the API key that authenticates the uploader. The key is read and never echoed in an answer.
/// </summary>
public sealed class UploadTokenRequest
{
    /// <summary>The handle of the staged upload (<see cref="UploadPreview.Token"/>).</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>The API key of the uploader while accounts are off (otherwise the signed-in session or the key of the request authenticates).</summary>
    public string? ApiKey { get; init; }

    /// <summary>The confirmation that the uploader may publish the package (required to publish, ignored when discarding).</summary>
    public bool AcceptTerms { get; init; }
}
