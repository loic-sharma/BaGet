using System;
using System.Collections.Generic;
using System.Linq;

namespace BaGet.Core;

/// <summary>
/// One grant or project role behind an access decision, for the admin pages that explain it.
/// </summary>
/// <param name="Source">Where it comes from, such as <c>Permission target 'Anyone reads every repository'</c>.</param>
/// <param name="PrincipalType">What kind of principal holds it.</param>
/// <param name="PrincipalId">The principal's id (user id, group or organization key).</param>
/// <param name="Principal">The principal in words (a user name, <c>group Developers</c>, <c>organization Contoso</c>).</param>
/// <param name="Granted">What the grant or role carries.</param>
/// <param name="Effective">What of it applies here: <see cref="Granted"/> minus Read when the Everyone group's Read is
/// gated off by the repository's anonymous-read switch.</param>
/// <param name="Packages">The package filter of the target, such as <c>Contoso.* except Contoso.Internal.*</c>; null for all.</param>
/// <param name="Note">Why <see cref="Effective"/> differs from <see cref="Granted"/>, if it does.</param>
public sealed record AccessReason(
    string Source,
    PrincipalType PrincipalType,
    string PrincipalId,
    string Principal,
    AccessAction Granted,
    AccessAction Effective,
    string? Packages,
    string? Note);

/// <summary>What a caller may do in a repository, and why (<see cref="IAccessEvaluator.ExplainAsync"/>).</summary>
/// <param name="Actions">Every action the caller holds.</param>
/// <param name="Override">A rule that grants everything or bypasses the model (administrator, global API key, no
/// access model), instead of <paramref name="Reasons"/>; null when the grants decide.</param>
/// <param name="Reasons">The grants and project roles that apply to the caller.</param>
public sealed record AccessExplanation(AccessAction Actions, string? Override, IReadOnlyList<AccessReason> Reasons)
{
    /// <summary>Whether the caller holds <paramref name="action"/>.</summary>
    public bool Has(AccessAction action) => (Actions & action) == action;
}

/// <summary>Who holds what in one repository (<see cref="IAccessEvaluator.DescribeAsync"/>).</summary>
/// <param name="Repository">The repository.</param>
/// <param name="AnonymousRead">The repository's anonymous-read switch.</param>
/// <param name="Project">The name of the project the repository belongs to, if any.</param>
/// <param name="ModelAvailable">False where there is no access model (Azure Table): everyone reads, any authenticated
/// caller writes, subject to ownership.</param>
/// <param name="Holders">Every grant and project role that applies to the repository.</param>
public sealed record RepositoryAccessSummary(
    RepositoryInfo Repository,
    bool AnonymousRead,
    string? Project,
    bool ModelAvailable,
    IReadOnlyList<AccessReason> Holders)
{
    /// <summary>The principals that effectively hold <paramref name="action"/>, without repeats.</summary>
    public IReadOnlyList<string> Who(AccessAction action)
        => Holders.Where(h => (h.Effective & action) == action).Select(h => h.Principal).Distinct(StringComparer.Ordinal).ToList();
}

/// <summary>
/// The same case-insensitive <c>*</c> globs the evaluator uses for repository names and package IDs, for the admin
/// pages' previews and test box: what they show is what the evaluator decides.
/// </summary>
public static class AccessPatterns
{
    /// <summary>Whether any of <paramref name="patterns"/> matches <paramref name="value"/>.</summary>
    /// <param name="patterns">The globs; blank entries are ignored.</param>
    /// <param name="value">A repository name or package ID.</param>
    /// <param name="emptyMeansAll">Whether no pattern at all matches everything (include lists) or nothing.</param>
    public static bool IsMatch(IEnumerable<string>? patterns, string? value, bool emptyMeansAll = false)
        => GlobSet.Parse(patterns, emptyMeansAll).IsMatch(value);

    /// <summary>
    /// Splits what an administrator typed (one pattern per line, or separated by commas or semicolons) into patterns.
    /// </summary>
    public static string[] Split(string? text)
        => (text ?? string.Empty)
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

    /// <summary>
    /// Whether a target with these patterns applies to <paramref name="packageId"/>, and which pattern decided: the
    /// evaluator's rule (an include pattern matches, or there is none, and no exclude pattern matches).
    /// </summary>
    public static PatternTest Test(IEnumerable<string>? include, IEnumerable<string>? exclude, string packageId)
    {
        var inc = (include ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
        var exc = (exclude ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();

        var excluded = exc.FirstOrDefault(p => IsMatch(new[] { p }, packageId));
        if (excluded != null) return new PatternTest(false, $"excluded by '{excluded}'");

        if (inc.Count == 0) return new PatternTest(true, "no include pattern, so every package is included");

        var included = inc.FirstOrDefault(p => IsMatch(new[] { p }, packageId));
        return included != null
            ? new PatternTest(true, $"included by '{included}'")
            : new PatternTest(false, "matches no include pattern");
    }

    /// <summary>The package filter in words, or null when the target applies to every package.</summary>
    public static string? DescribeScope(IEnumerable<string>? include, IEnumerable<string>? exclude)
    {
        var inc = (include ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        var exc = (exclude ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if ((inc.Length == 0 || inc.Contains("*")) && exc.Length == 0) return null;

        var text = inc.Length == 0 ? "*" : string.Join(", ", inc);
        return exc.Length == 0 ? text : text + " except " + string.Join(", ", exc);
    }
}

/// <summary>The outcome of <see cref="AccessPatterns.Test"/>.</summary>
/// <param name="Matches">Whether the target applies to the package.</param>
/// <param name="Explanation">Which pattern decided, in words.</param>
public sealed record PatternTest(bool Matches, string Explanation);

/// <summary>A group as the admin pages list it.</summary>
public sealed record GroupSummary(int Key, string Name, string? Description, bool IsBuiltIn, int MemberCount);

/// <summary>A member of a group.</summary>
public sealed record GroupMemberInfo(string UserId, string UserName);

/// <summary>A grant, with the target and principal in words.</summary>
public sealed record GrantInfo(string Target, PrincipalType PrincipalType, string PrincipalId, string Principal, AccessAction Actions);

/// <summary>A group with its members and the grants naming it.</summary>
public sealed record GroupDetails(GroupSummary Group, IReadOnlyList<GroupMemberInfo> Members, IReadOnlyList<GrantInfo> Grants);

/// <summary>A permission target as the admin pages list it.</summary>
public sealed record TargetSummary(
    int Key,
    string Name,
    IReadOnlyList<string> RepositoryPatterns,
    IReadOnlyList<string> IncludePatterns,
    IReadOnlyList<string> ExcludePatterns,
    int? ProjectKey,
    string? ProjectName,
    bool IsBuiltIn,
    int GrantCount);

/// <summary>A permission target with its grants.</summary>
public sealed record TargetDetails(TargetSummary Target, IReadOnlyList<GrantInfo> Grants);

/// <summary>A project as the admin pages list it.</summary>
public sealed record ProjectSummary(int Key, string Name, string? DisplayName, int RepositoryCount, int MemberCount);

/// <summary>A member of a project.</summary>
public sealed record ProjectMemberInfo(PrincipalType PrincipalType, string PrincipalId, string Principal, ProjectRole Role);

/// <summary>A project with its repositories, members and targets.</summary>
public sealed record ProjectDetails(
    ProjectSummary Project,
    IReadOnlyList<string> Repositories,
    IReadOnlyList<ProjectMemberInfo> Members,
    IReadOnlyList<TargetSummary> Targets);

/// <summary>A role a user holds in a project, directly or through a group.</summary>
public sealed record UserProjectRole(string Project, ProjectRole Role, string Via);

/// <summary>Which audit entries to list; every filter is optional.</summary>
/// <param name="Actor">The user name that made the change (exact, case-insensitive).</param>
/// <param name="Subject">What it happened to (contains, case-insensitive).</param>
/// <param name="From">Earliest time (UTC, inclusive).</param>
/// <param name="To">Latest time (UTC, exclusive).</param>
public sealed record AuditFilter(string? Actor = null, string? Subject = null, DateTime? From = null, DateTime? To = null);
