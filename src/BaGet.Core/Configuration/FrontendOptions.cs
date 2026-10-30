namespace BaGet.Core;

/// <summary>
/// The web front ends the host serves (<c>Frontend:*</c>), read by the host at startup.
/// </summary>
[RestartRequired]
public class FrontendOptions
{
    /// <summary>
    /// A folder holding a built single-page application (it contains <c>index.html</c>) that BaGet serves under <see cref="SpaBasePath"/>. Empty
    /// (the default) serves none. Relative paths are resolved against the content root.
    /// </summary>
    public string? SpaPath { get; set; }

    /// <summary>
    /// The URL path the single-page application is served under, <c>/app</c> by default. A GET below it that matches no file answers
    /// <c>index.html</c>, so the application's own router works; it never shadows <c>/api</c>, the protocols or the MVC screens.
    /// </summary>
    public string SpaBasePath { get; set; } = "/app";

    /// <summary>
    /// Whether the server-rendered MVC screens are served. On by default; off, the host serves the APIs only (and the single-page application, if any).
    /// </summary>
    public bool Mvc { get; set; } = true;

    /// <summary>
    /// Whether the links in account emails (confirm email, reset password) point at the single-page application under <see cref="SpaBasePath"/>
    /// instead of the server-rendered screens: <c>{SpaBasePath}/confirm-email?userId=&amp;code=[&amp;email=]</c>, <c>{SpaBasePath}/reset-password?userId=&amp;code=</c>,
    /// <c>{SpaBasePath}/sign-in</c> (the same query values as the screens' links). Unset (the default) follows <see cref="Mvc"/>: on when the screens are off.
    /// </summary>
    public bool? SpaLinks { get; set; }

    /// <summary>
    /// The absolute address of a single-page application hosted <b>elsewhere</b> (<c>https://app.example.com</c>, or with a path): account emails then link to
    /// <c>{SpaUrl}/confirm-email</c>, <c>{SpaUrl}/reset-password</c> and the cookie's login path is <c>{SpaUrl}/sign-in</c>. Takes precedence over <see cref="SpaLinks"/>.
    /// </summary>
    public string? SpaUrl { get; set; }

    /// <summary><see cref="SpaBasePath"/> as a path with a leading slash and none at the end (<c>/</c> for the root).</summary>
    public string NormalizedBasePath()
    {
        var trimmed = (SpaBasePath ?? string.Empty).Trim().TrimEnd('/');

        return trimmed.Length == 0 ? "/" : trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    /// <summary>The base path of the application's email-link routes, or null when emails link to the server-rendered screens.</summary>
    public string? EmailLinkBase
    {
        get
        {
            // An application hosted elsewhere, by its absolute address.
            if (!string.IsNullOrWhiteSpace(SpaUrl)) return SpaUrl.Trim().TrimEnd('/');

            // The one BaGet serves (Frontend:SpaPath), or, on the operator's word (SpaLinks=true), one a reverse proxy serves under the base path. With the screens off and
            // neither, nothing is served at the base path: a link there would be a 404, so there is no base (startup refuses to mail then).
            if (SpaLinks == true || (SpaLinks ?? !Mvc) && !string.IsNullOrWhiteSpace(SpaPath)) return NormalizedBasePath().TrimEnd('/');

            return null;
        }
    }

    /// <summary>Whether account emails would link to a page that does not exist: the screens are off and no application is named to link to.</summary>
    public bool EmailLinksWouldBeDead => !Mvc && EmailLinkBase == null;
}
