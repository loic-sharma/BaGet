using System.Text.Json.Serialization;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Repositories;

/// <summary>
/// The fields of a remote repository's upstream settings as the create and edit pages post them (prefix
/// <c>Remote</c>). The password is never rendered back: a blank one on save keeps the stored one.
/// </summary>
public class RemoteFormModel
{
    /// <summary>
    /// For display: the format of the repository being edited (an npm repository has no protocol choice and a different URL and
    /// credential wording). Never read back: the repository's own format decides.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RepositoryFormat Format { get; set; } = RepositoryFormat.NuGet;

    /// <summary>The upstream URL (V3 <c>index.json</c> or V2 feed, or an npm registry's base URL).</summary>
    public string? Url { get; set; }

    /// <summary>The upstream's protocol (NuGet only; an npm repository speaks the npm registry protocol).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RemoteProtocol Protocol { get; set; } = RemoteProtocol.V3;

    /// <summary>The HTTP Basic user name, if the upstream needs one.</summary>
    public string? Username { get; set; }

    /// <summary>
    /// The HTTP Basic password or token; blank keeps the stored one. Input only: a screen never sends it back (the use cases clear it from every model they return).
    /// </summary>
    public string? Password { get; set; }

    /// <summary>Remove the stored password on save.</summary>
    public bool ClearPassword { get; set; }

    /// <summary>How long the upstream's answers are reused, in seconds (0 = ask every time).</summary>
    public int MetadataTtlSeconds { get; set; } = 600;

    /// <summary>How long "not found" is remembered, in seconds (0 = ask every time).</summary>
    public int NegativeTtlSeconds { get; set; } = 60;

    /// <summary>The download timeout in seconds.</summary>
    public int DownloadTimeoutSeconds { get; set; } = 600;

    /// <summary>Never contact the upstream.</summary>
    public bool Offline { get; set; }

    /// <summary>Keep what is fetched (off = pass-through).</summary>
    public bool StoreLocally { get; set; } = true;

    /// <summary>For display: whether a password is stored.</summary>
    public bool HasPassword { get; set; }

    /// <summary>For display: URL, protocol and timeout are fixed because <c>Mirror:*</c> sets them.</summary>
    public bool EndpointFromMirror { get; set; }

    /// <summary>The settings these fields describe.</summary>
    public RemoteRepositorySettings ToSettings() => new()
    {
        Url = Url?.Trim(),
        Protocol = Protocol,
        Username = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim(),
        Password = string.IsNullOrEmpty(Password) ? null : Password,
        MetadataTtlSeconds = MetadataTtlSeconds,
        NegativeTtlSeconds = NegativeTtlSeconds,
        DownloadTimeoutSeconds = DownloadTimeoutSeconds,
        Offline = Offline,
        StoreLocally = StoreLocally,
    };

    /// <summary>The fields for an existing remote repository.</summary>
    public static RemoteFormModel From(RemoteRepositoryDetail detail) => new()
    {
        Format = detail.Repository.Format,
        Url = detail.Settings.Url,
        Protocol = detail.Settings.Protocol,
        Username = detail.Settings.Username,
        MetadataTtlSeconds = detail.Settings.MetadataTtlSeconds,
        NegativeTtlSeconds = detail.Settings.NegativeTtlSeconds,
        DownloadTimeoutSeconds = detail.Settings.DownloadTimeoutSeconds,
        Offline = detail.Settings.Offline,
        StoreLocally = detail.Settings.StoreLocally,
        HasPassword = detail.HasPassword,
        EndpointFromMirror = detail.IsMirror,
    };
}
