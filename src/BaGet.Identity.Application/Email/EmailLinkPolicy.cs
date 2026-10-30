using System;
using BaGet.Core;

namespace BaGet.Identity.Application.Email;

/// <summary>
/// Where the links in account emails may point. A link in a password-reset or confirmation
/// email must never be built from a request's <c>Host</c> header unless that header is
/// known to be one of the server's own: otherwise anyone can request a reset for a victim
/// with a forged <c>Host</c> and have the victim's mail carry a link to the attacker.
/// </summary>
public static class EmailLinkPolicy
{
    /// <summary>
    /// Fails startup on a <see cref="SiteOptions.PublicUrl"/> that is set but is not an
    /// absolute http or https URL.
    /// </summary>
    public static void ValidatePublicUrl(string? publicUrl)
    {
        if (string.IsNullOrWhiteSpace(publicUrl)) return;

        if (!TryParsePublicUrl(publicUrl, out _))
        {
            throw new InvalidOperationException(
                $"The 'Site:PublicUrl' config is invalid: '{publicUrl}'. Use an absolute http or https URL "
                + "such as \"https://packages.example.com\".");
        }
    }

    public static bool TryParsePublicUrl(string? publicUrl, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(publicUrl)) return false;

        if (!Uri.TryCreate(publicUrl.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(parsed.Host)) return false;

        uri = parsed;

        return true;
    }

    /// <summary>
    /// Links can be built only from the configured public URL. The request's host and scheme
    /// are attacker-influenced (a forged Host, a spoofed X-Forwarded-Proto) even when
    /// "AllowedHosts" restricts the host, so they are never used.
    /// </summary>
    public static bool CanBuildLinks(string? publicUrl) => TryParsePublicUrl(publicUrl, out _);

    public const string UnavailableWarning =
        "Email is configured, but \"Site:PublicUrl\" is not set, so links in emails cannot be trusted to point "
        + "at this server. Email, and everything that needs it (password recovery, email confirmation, "
        + "contact owners), is switched off. Set \"Site:PublicUrl\" (for example "
        + "\"https://packages.example.com\") to enable it.";
}
