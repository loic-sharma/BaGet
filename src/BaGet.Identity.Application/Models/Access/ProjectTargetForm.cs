namespace BaGet.Identity.Application.Models.Access;

/// <summary>The fields of a project's new permission target and its test box.</summary>
/// <param name="TargetName">The new target's name.</param>
/// <param name="RepositoryPatterns">Repository globs, one per line (they only ever reach the project's repositories).</param>
/// <param name="IncludePatterns">Package ID globs the target applies to, one per line.</param>
/// <param name="ExcludePatterns">Package ID globs the target leaves out, one per line.</param>
/// <param name="TestPackage">A package ID to test the patterns with.</param>
public sealed record ProjectTargetForm(string? TargetName, string? RepositoryPatterns, string? IncludePatterns, string? ExcludePatterns, string? TestPackage);
