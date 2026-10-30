using System.Text.Json.Serialization;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>The fields of the permission-target form (new target, or the patterns of an existing one) and its test box.</summary>
/// <param name="Name">The new target's name (ignored for an existing target: the route names it).</param>
/// <param name="RepositoryPatterns">Repository names or <c>*</c> globs, one per line.</param>
/// <param name="IncludePatterns">Package ID globs the target applies to, one per line; none means all.</param>
/// <param name="ExcludePatterns">Package ID globs the target leaves out, one per line.</param>
/// <param name="Project">For a new target: the project that administers it, or null.</param>
/// <param name="TestPackage">A package ID to test the patterns with.</param>
/// <param name="PrincipalType">For an existing target: the principal kind in the grant form (kept across a preview).</param>
/// <param name="PrincipalName">For an existing target: the principal name in the grant form (kept across a preview).</param>
/// <param name="Password">The administrator's password (saving the patterns of an existing target).</param>
public sealed record PermissionTargetForm(
    string? Name = null,
    string? RepositoryPatterns = null,
    string? IncludePatterns = null,
    string? ExcludePatterns = null,
    string? Project = null,
    string? TestPackage = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PrincipalType? PrincipalType = null,
    string? PrincipalName = null,
    string? Password = null);
