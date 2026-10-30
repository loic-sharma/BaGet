using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace BaGet.Core;

/// <summary>
/// Accepts only a Markdown file path (*.md) without a ".." segment. The legal pages are public,
/// so a path to any other file (appsettings.json, the settings overrides file) would publish it.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MarkdownFilePathAttribute : ValidationAttribute
{
    /// <summary>Whether <paramref name="path"/> is empty or an allowed Markdown file path.</summary>
    public static bool IsAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;

        if (!path.Trim().EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;

        return !path.Split('/', (char)92).Any(segment => segment.Trim() == "..");
    }

    /// <inheritdoc />
    #pragma warning disable CS8620
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        => value is not string path || IsAllowed(path)
            ? ValidationResult.Success
            : new ValidationResult(
                $"{validationContext.MemberName} must be a Markdown file (.md) path without \"..\" segments.",
                new[] { validationContext.MemberName });
    #pragma warning restore CS8620
}


/// <summary>
/// Settings of the web site's chrome. BaGet ships no legal text: the operator supplies
/// the Terms of Use and Privacy Policy as Markdown files, and the footer links to them
/// only while they are set.
/// </summary>
public class SiteOptions
{
    /// <summary>
    /// A Markdown file (.md), absolute or relative to the content root, served at /terms.
    /// </summary>
    [MarkdownFilePath]
    public string? TermsOfUsePath { get; set; }

    /// <summary>
    /// A Markdown file (.md), absolute or relative to the content root, served at /privacy.
    /// </summary>
    [MarkdownFilePath]
    public string? PrivacyPolicyPath { get; set; }

    /// <summary>
    /// The absolute http or https URL users reach this server at, for example
    /// "https://packages.example.com" (a path is allowed when served under one). Links in
    /// account emails are built only from it. Without it they can only be built from the
    /// request host when "AllowedHosts" restricts the hosts; with the default "*" email is
    /// switched off, because a forged Host header would otherwise decide where a password
    /// reset link points.
    /// </summary>
    [RestartRequired]
    public string? PublicUrl { get; set; }

    /// <summary>
    /// Show Gravatar avatars. Each avatar is a request to gravatar.com carrying a hash of the
    /// account's email, so a private feed can turn this off and get initial-letter placeholders.
    /// </summary>
    public bool UseGravatar { get; set; } = true;

    /// <summary>
    /// Set by the host from <c>Frontend:SpaLinks</c> / <c>Frontend:Mvc</c>: the base path of the single-page application that account emails link to
    /// (<c>/app</c>), or null for the server-rendered screens. Not a setting.
    /// </summary>
    [HiddenSetting]
    public string? SpaLinkBase { get; set; }
}
