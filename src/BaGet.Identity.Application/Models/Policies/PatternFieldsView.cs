namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What the pattern fields partial (<c>_PatternFields</c>) shows.</summary>
/// <param name="Repositories">The repository patterns, one per line.</param>
/// <param name="Include">The include patterns, one per line.</param>
/// <param name="Exclude">The exclude patterns, one per line.</param>
/// <param name="TestPackage">The test box package ID.</param>
/// <param name="Preview">What the patterns select, or null before the first preview.</param>
public sealed record PatternFieldsView(string? Repositories, string? Include, string? Exclude, string? TestPackage, PatternPreview? Preview);
