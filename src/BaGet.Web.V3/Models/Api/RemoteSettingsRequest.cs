using System;
using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>An upstream, as the API takes it. Fields left out keep the defaults of a new remote repository.</summary>
public sealed class RemoteSettingsRequest
{
    /// <summary>The upstream URL (an npm registry's, Maven repository's or Docker registry's base; a NuGet V3 <c>index.json</c> or V2 feed).</summary>
    public string? Url { get; set; }

    /// <summary><c>V3</c> or <c>V2</c> for NuGet; the other formats fix it.</summary>
    public string? Protocol { get; set; }

    /// <summary>The HTTP Basic user name (a password with no user name is a bearer token).</summary>
    public string? Username { get; set; }

    /// <summary>The password or token; on update blank keeps the stored one.</summary>
    public string? Password { get; set; }

    /// <summary>On update: remove the stored password.</summary>
    public bool ClearPassword { get; set; }

    /// <summary>How long upstream answers are reused, in seconds.</summary>
    public int? MetadataTtlSeconds { get; set; }

    /// <summary>How long "not found" is remembered, in seconds.</summary>
    public int? NegativeTtlSeconds { get; set; }

    /// <summary>The download timeout in seconds.</summary>
    public int? DownloadTimeoutSeconds { get; set; }

    /// <summary>Never contact the upstream.</summary>
    public bool Offline { get; set; }

    /// <summary>Keep what is fetched (false = pass-through).</summary>
    public bool? StoreLocally { get; set; }

    internal RemoteRepositorySettings ToSettings(RemoteRepositorySettings? start = null)
    {
        var settings = start ?? new RemoteRepositorySettings();
        settings.Url = Url?.Trim();
        if (!string.IsNullOrWhiteSpace(Protocol))
        {
            if (!Enum.TryParse<RemoteProtocol>(Protocol, ignoreCase: true, out var protocol))
            {
                throw new ArgumentException($"Unknown protocol '{Protocol}'. Use {string.Join(", ", Enum.GetNames(typeof(RemoteProtocol)))}.");
            }

            settings.Protocol = protocol;
        }

        settings.Username = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();
        settings.Password = string.IsNullOrEmpty(Password) ? null : Password;
        if (MetadataTtlSeconds is int metadata) settings.MetadataTtlSeconds = metadata;
        if (NegativeTtlSeconds is int negative) settings.NegativeTtlSeconds = negative;
        if (DownloadTimeoutSeconds is int timeout) settings.DownloadTimeoutSeconds = timeout;
        settings.Offline = Offline;
        if (StoreLocally is bool store) settings.StoreLocally = store;

        return settings;
    }
}
