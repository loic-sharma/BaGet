using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.Hosting;
using BaGet.Web.Application.Models.Packages;

namespace BaGet.Web.Application.Screens;

/// <summary>
/// The workflow behind the legal pages: reads the operator-supplied Markdown file. Used by <c>LegalController</c>. BaGet ships no legal text of its own: there is no content
/// while the file is not configured, is not an allowed Markdown path (<see cref="MarkdownFilePathAttribute.IsAllowed"/>) or does not exist.
/// </summary>
internal static class LegalContent
{
    /// <summary>The rendered file, or null when the page is a 404.</summary>
    public static async Task<HtmlString?> ReadAsync(string? configuredPath, IHostEnvironment environment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuredPath) || !MarkdownFilePathAttribute.IsAllowed(configuredPath)) return null;

        // Path.Combine keeps an absolute path as it is.
        var fullPath = Path.Combine(environment.ContentRootPath, configuredPath);
        if (!File.Exists(fullPath)) return null;

        return ReadmeMarkdown.ToSafeHtml(await File.ReadAllTextAsync(fullPath, cancellationToken));
    }
}
