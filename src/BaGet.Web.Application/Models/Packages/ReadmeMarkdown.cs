using System;
using System.Linq;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Html;

namespace BaGet.Web.Application.Models.Packages;

/// <summary>
/// Renders a package's readme. The package page and the upload preview use one pipeline
/// so that what an uploader is shown is what readers will be shown.
/// </summary>
public static class ReadmeMarkdown
{
    // Readmes are written by whoever pushed the package, so they are untrusted: raw HTML in
    // the source is escaped rather than rendered, and links and images with a script scheme
    // are neutralised. Markdown itself (headings, tables, code, ordinary links) is unaffected.
    //
    // The extensions are listed rather than taken from UseAdvancedExtensions(), which includes
    // GenericAttributes: "# Hi {onmouseover=...}" would emit an event handler. MediaLinks
    // (iframes and players) and the maths and diagram blocks are left out too: a readme does
    // not need them.
    private static readonly MarkdownPipeline Pipeline = CreatePipeline(genericAttributes: false);

    internal static MarkdownPipeline CreatePipeline(bool genericAttributes)
    {
        var builder = new MarkdownPipelineBuilder()
            .UseAbbreviations()
            .UseAutoIdentifiers()
            .UseAutoLinks()
            .UseDefinitionLists()
            .UseEmphasisExtras()
            .UseFigures()
            .UseFootnotes()
            .UseGridTables()
            .UseListExtras()
            .UsePipeTables()
            .UseTaskLists()
            .DisableHtml();

        // Only for the test that proves the sanitiser below holds even if the extension is on.
        if (genericAttributes) builder.UseGenericAttributes();

        return builder.Build();
    }

    private static readonly string[] UnsafeSchemes = { "javascript:", "vbscript:", "data:" };

    public static HtmlString? ToSafeHtml(string? markdown) => ToHtml(markdown);

    public static HtmlString? ToHtml(string? markdown) => ToHtml(markdown, Pipeline);

    internal static HtmlString? ToHtml(string? markdown, MarkdownPipeline pipeline)
    {
        if (markdown == null) return null;

        var document = Markdown.Parse(markdown, pipeline);
        RemoveUnsafeAttributes(document);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (IsUnsafe(link.Url)) link.Url = "#";
        }

        foreach (var autolink in document.Descendants<AutolinkInline>())
        {
            if (IsUnsafe(autolink.Url)) autolink.Url = "#";
        }

        return new HtmlString(document.ToHtml(pipeline));
    }

    /// <summary>
    /// Defence in depth: whatever put attributes on a node, event handlers (on*), inline
    /// styles and the attributes that carry a URL or markup of their own never reach the page.
    /// </summary>
    private static void RemoveUnsafeAttributes(MarkdownDocument document)
    {
        foreach (var node in document.Descendants())
        {
            var properties = node.TryGetAttributes()?.Properties;
            properties?.RemoveAll(pair => IsUnsafeAttribute(pair.Key));
        }
    }

    private static bool IsUnsafeAttribute(string? name)
    {
        var cleaned = new string((name ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());

        return cleaned.StartsWith("on", StringComparison.OrdinalIgnoreCase)
               || UnsafeAttributes.Contains(cleaned, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] UnsafeAttributes = { "style", "formaction", "form", "srcdoc", "href", "src", "action", "xlink:href", "data" };

    private static bool IsUnsafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        // Browsers ignore whitespace and control characters inside the scheme.
        var cleaned = new string(url.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());

        return UnsafeSchemes.Any(scheme => cleaned.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));
    }
}