using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace BaGet.Tests.Contracts;

/// <summary>
/// Reduces a rendered page to what a user, a bookmark or a script depends on: its title, every form (method, action with its
/// <c>?handler=</c>, field names and types, antiforgery token present) and every link to a BaGet page. Texts, hidden values,
/// tokens, dates and ids are left out or normalized so the summary is the same on every run.
/// </summary>
internal static class UiHtml
{
    internal sealed record Form(string Method, string Action, bool Antiforgery, IReadOnlyList<string> Fields);

    internal sealed record Summary(string Title, IReadOnlyList<Form> Forms, IReadOnlyList<string> Links);

    private static readonly RegexOptions Options = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex AssetPath = new(@"(^|/)(_content|lib)/|\.(css|js|map|ico|png|svg|jpe?g|gif|woff2?|ttf|eot)(\?|#|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static Summary Summarize(string html)
    {
        var title = Regex.Match(html, "<title[^>]*>(.*?)</title>", Options);
        var forms = new List<Form>();
        foreach (Match form in Regex.Matches(html, @"<form\b([^>]*)>(.*?)</form>", Options))
        {
            var attributes = Attributes(form.Groups[1].Value);
            var fields = new SortedSet<string>(StringComparer.Ordinal);
            var antiforgery = false;
            foreach (Match element in Regex.Matches(form.Groups[2].Value, @"<(input|select|textarea|button)\b([^>]*)>", Options))
            {
                var kind = element.Groups[1].Value.ToLowerInvariant();
                var fieldAttributes = Attributes(element.Groups[2].Value);
                fieldAttributes.TryGetValue("name", out var name);
                fieldAttributes.TryGetValue("type", out var type);
                if (name == "__RequestVerificationToken") antiforgery = true;

                if (kind == "button")
                {
                    // A submit button may post to another handler of the page.
                    if (fieldAttributes.TryGetValue("formaction", out var formAction)) fields.Add("button@" + Normalize(formAction));
                    if (name != null) fields.Add($"button:{name}");
                    continue;
                }

                if (name == null || name == "__RequestVerificationToken") continue;
                fields.Add($"{name}:{(kind == "input" ? (type ?? "text").ToLowerInvariant() : kind)}");
            }

            attributes.TryGetValue("method", out var method);
            attributes.TryGetValue("action", out var action);
            forms.Add(new Form((method ?? "get").ToUpperInvariant(), Normalize(action ?? string.Empty), antiforgery, fields.ToList()));
        }

        var links = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match anchor in Regex.Matches(html, @"<a\b([^>]*)>", Options))
        {
            if (!Attributes(anchor.Groups[1].Value).TryGetValue("href", out var href)) continue;
            href = Normalize(href);
            if (href.Length == 0 || href.StartsWith('#') || AssetPath.IsMatch(href)) continue;
            if (Regex.IsMatch(href, @"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
            links.Add(href);
        }

        return new Summary(Regex.Replace(WebUtility.HtmlDecode(title.Groups[1].Value), @"\s+", " ").Trim(), forms, links.ToList());
    }

    private static Dictionary<string, string> Attributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(text, @"([\w:-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", Options))
        {
            var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;
            result.TryAdd(match.Groups[1].Value, WebUtility.HtmlDecode(value));
        }

        return result;
    }

    /// <summary>Strips the host and replaces what changes from run to run (ids, hashes, dates) with a placeholder.</summary>
    public static string Normalize(string value)
    {
        value = Regex.Replace(value, @"^https?://localhost(:\d+)?", string.Empty, RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "{guid}", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\d{4}-\d{2}-\d{2}T[\d:.]+(Z|[+-]\d{2}:?\d{2})?", "{date}");
        value = Regex.Replace(value, "[0-9a-f]{32,}", "{hex}", RegexOptions.IgnoreCase);

        return value;
    }
}
