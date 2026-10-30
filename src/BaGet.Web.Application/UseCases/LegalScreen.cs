using BaGet.Web.Application.Models.Packages;

namespace BaGet.Web.Application.UseCases;

/// <summary>What a legal page (Terms of Use, Privacy Policy) shows: its heading and the operator's Markdown rendered to safe HTML.</summary>
/// <param name="Title">The heading and page title.</param>
/// <param name="ContentHtml">The operator's Markdown file as HTML (raw HTML in the source is escaped, script links neutralised: <see cref="ReadmeMarkdown.ToSafeHtml"/>).</param>
public sealed record LegalScreen(string Title, string ContentHtml);
