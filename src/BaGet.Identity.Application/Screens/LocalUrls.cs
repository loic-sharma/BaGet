using System;

namespace BaGet.Identity.Application.Screens;

/// <summary>
/// The rule for a return URL after a sign-in: only a local address is followed, so a sign-in link cannot send the user elsewhere.
/// It is the rule of <c>IUrlHelper.IsLocalUrl</c> (an absolute path, or an application-relative <c>~/</c> path, with no scheme, host,
/// <c>//</c>, <c>/\</c> or control character), kept here because the use cases have no <c>IUrlHelper</c>.
/// </summary>
public static class LocalUrls
{
    /// <summary>The site root, written application-relative (the view prefixes the path base).</summary>
    public const string Root = "~/";

    private const char Backslash = (char)92;

    /// <summary>Whether <paramref name="url"/> is a local address.</summary>
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        if (url[0] == '/')
        {
            if (url.Length == 1) return true;
            if (url[1] == '/' || url[1] == Backslash) return false;

            return !HasControlCharacter(url.AsSpan(1));
        }

        if (url[0] == '~' && url.Length > 1 && url[1] == '/')
        {
            if (url.Length == 2) return true;
            if (url[2] == '/' || url[2] == Backslash) return false;

            return !HasControlCharacter(url.AsSpan(2));
        }

        return false;
    }

    /// <summary><paramref name="returnUrl"/> when it is local, else the site root.</summary>
    public static string Target(string? returnUrl) => IsLocal(returnUrl) ? returnUrl! : Root;

    /// <summary>
    /// A local address written application-relative (<c>~/x</c>), so the redirect adds the path base; a root-relative <c>/x</c> written by a
    /// view or a caller that does not know the path base would otherwise leave the application. Other values are returned unchanged.
    /// </summary>
    public static string AppRelative(string url) => url.Length > 0 && url[0] == '/' && IsLocal(url) ? "~" + url : url;

    private static bool HasControlCharacter(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (char.IsControl(c)) return true;
        }

        return false;
    }
}
