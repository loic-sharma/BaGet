namespace BaGet.Web.V3.Models.Api;

/// <summary>A remote repository's upstream; the password is never returned.</summary>
public sealed record RemoteSettingsDto(
    string? Url,
    string Protocol,
    string? Username,
    bool HasPassword,
    int MetadataTtlSeconds,
    int NegativeTtlSeconds,
    int DownloadTimeoutSeconds,
    bool Offline,
    bool StoreLocally,
    bool IsMirror);
