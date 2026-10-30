using System.IO;
using System.Text;
using System.Text.Json;
using BaGet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Web.V3.Oci;

/// <summary>
/// The HTTP shape of the OCI Distribution Spec: the JSON error body, the status each error code is answered with, and the
/// <c>WWW-Authenticate</c> challenge that starts the token flow of <c>docker login</c>.
/// </summary>
public static class OciHttp
{
    /// <summary>The header every registry response carries so a client knows it speaks to a v2 registry.</summary>
    public const string ApiVersionHeader = "Docker-Distribution-API-Version";

    /// <summary>The value of <see cref="ApiVersionHeader"/>.</summary>
    public const string ApiVersion = "registry/2.0";

    /// <summary>The header carrying a blob's or manifest's digest.</summary>
    public const string DigestHeader = "Docker-Content-Digest";

    /// <summary>The header carrying the id of an upload session.</summary>
    public const string UploadUuidHeader = "Docker-Upload-UUID";

    /// <summary>The wire name of an error code (<c>BLOB_UNKNOWN</c>).</summary>
    public static string Name(OciErrorCode code) => code switch
    {
        OciErrorCode.BlobUnknown => "BLOB_UNKNOWN",
        OciErrorCode.BlobUploadInvalid => "BLOB_UPLOAD_INVALID",
        OciErrorCode.BlobUploadUnknown => "BLOB_UPLOAD_UNKNOWN",
        OciErrorCode.DigestInvalid => "DIGEST_INVALID",
        OciErrorCode.ManifestBlobUnknown => "MANIFEST_BLOB_UNKNOWN",
        OciErrorCode.ManifestInvalid => "MANIFEST_INVALID",
        OciErrorCode.ManifestUnknown => "MANIFEST_UNKNOWN",
        OciErrorCode.NameInvalid => "NAME_INVALID",
        OciErrorCode.NameUnknown => "NAME_UNKNOWN",
        OciErrorCode.SizeInvalid => "SIZE_INVALID",
        OciErrorCode.TagInvalid => "TAG_INVALID",
        OciErrorCode.Unsupported => "UNSUPPORTED",
        OciErrorCode.RangeInvalid => "BLOB_UPLOAD_INVALID",
        OciErrorCode.PaginationNumberInvalid => "PAGINATION_NUMBER_INVALID",
        OciErrorCode.ManifestTooLarge => "MANIFEST_INVALID",
        OciErrorCode.Denied => "DENIED",
        OciErrorCode.UpstreamUnavailable => "UNAVAILABLE",
        _ => "UNSUPPORTED",
    };

    /// <summary>The HTTP status an error code is answered with.</summary>
    public static int Status(OciErrorCode code) => code switch
    {
        OciErrorCode.BlobUnknown or OciErrorCode.BlobUploadUnknown or OciErrorCode.ManifestUnknown or OciErrorCode.NameUnknown => StatusCodes.Status404NotFound,
        OciErrorCode.Unsupported => StatusCodes.Status405MethodNotAllowed,
        OciErrorCode.RangeInvalid => StatusCodes.Status416RangeNotSatisfiable,
        OciErrorCode.ManifestTooLarge => StatusCodes.Status413PayloadTooLarge,
        OciErrorCode.Denied => StatusCodes.Status403Forbidden,
        OciErrorCode.UpstreamUnavailable => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>The error response for a registry refusal.</summary>
    public static IActionResult Error(OciException exception) => Error(Status(exception.Code), Name(exception.Code), exception.Message, exception.Detail);

    /// <summary>An error response: <c>{"errors":[{"code","message","detail"}]}</c>.</summary>
    public static IActionResult Error(int status, string code, string message, string? detail = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("errors");
            writer.WriteStartObject();
            writer.WriteString("code", code);
            writer.WriteString("message", message);
            if (detail != null) writer.WriteString("detail", detail);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return new ContentResult { StatusCode = status, ContentType = "application/json", Content = Encoding.UTF8.GetString(stream.ToArray()) };
    }

    /// <summary>
    /// The <c>WWW-Authenticate</c> value that sends a client to the token endpoint:
    /// <c>Bearer realm="https://host/v2/token",service="BaGet",scope="repository:libs/app:pull,push"</c>, with
    /// <paramref name="error"/> (<c>insufficient_scope</c>, <c>invalid_token</c>) when a token was presented and refused.
    /// </summary>
    public static string Challenge(string tokenUrl, string service, string? scope, string? error)
    {
        var value = new StringBuilder("Bearer realm=\"").Append(Escape(tokenUrl)).Append("\",service=\"").Append(Escape(service)).Append('"');
        if (scope != null) value.Append(",scope=\"").Append(Escape(scope)).Append('"');
        if (error != null) value.Append(",error=\"").Append(error).Append('"');

        return value.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>The action words a challenge asks for, given what the request needs (a push also pulls, as Docker asks).</summary>
    public static OciActions ChallengeActions(OciActions required) => required == OciActions.Push ? OciActions.Pull | OciActions.Push : required;

    /// <summary>
    /// The action an OCI request needs, from its method and what it addresses: a pull reads manifests, tags, blobs and
    /// referrers; everything on an upload and every manifest <c>PUT</c> is a push; <c>DELETE</c> of a blob or manifest is a delete.
    /// A combination the specification does not define needs nothing (null) and is refused: there is no default to allow.
    /// </summary>
    public static OciActions? Required(string method, OciPathKind kind)
    {
        var read = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);

        return kind switch
        {
            OciPathKind.Blob when read => OciActions.Pull,
            OciPathKind.Blob when HttpMethods.IsDelete(method) => OciActions.Delete,
            OciPathKind.Manifest when read => OciActions.Pull,
            OciPathKind.Manifest when HttpMethods.IsPut(method) => OciActions.Push,
            OciPathKind.Manifest when HttpMethods.IsDelete(method) => OciActions.Delete,
            OciPathKind.TagList when read => OciActions.Pull,
            OciPathKind.Referrers when read => OciActions.Pull,
            OciPathKind.UploadStart when HttpMethods.IsPost(method) => OciActions.Push,
            OciPathKind.Upload when read || HttpMethods.IsPatch(method) || HttpMethods.IsPut(method) || HttpMethods.IsDelete(method) => OciActions.Push,
            _ => null,
        };
    }
}
