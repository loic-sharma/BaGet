using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Models.Policies;

namespace BaGet.Identity.Application.UseCases.Access;

/// <summary>The parts of the access screens that read and write pattern and action text; shared by the use cases and the controller bases.</summary>
public static class AccessPatternSupport
{
    /// <summary>The single actions, in display order.</summary>
    public static readonly AccessAction[] AllActions =
    {
        AccessAction.Read, AccessAction.Annotate, AccessAction.Deploy, AccessAction.Delete, AccessAction.Manage,
    };

    /// <summary>Parses the ticked action boxes (names of <see cref="AccessAction"/> values).</summary>
    public static AccessAction ParseActions(IEnumerable<string>? names)
    {
        var actions = AccessAction.None;
        foreach (var name in names ?? Array.Empty<string>())
        {
            if (Enum.TryParse<AccessAction>(name, ignoreCase: true, out var action) && action != AccessAction.All) actions |= action;
        }

        return actions;
    }

    /// <summary>The actions in <paramref name="actions"/> as words, or "none".</summary>
    public static string Words(AccessAction actions)
        => actions == AccessAction.None ? "none" : string.Join(", ", AllActions.Where(a => (actions & a) == a));

    /// <summary>Patterns one per line, the way the editors show them.</summary>
    public static string Lines(IEnumerable<string> patterns) => string.Join("\n", patterns);

    /// <summary>The package filter and repository patterns as one line a person can read back.</summary>
    public static string Join(IEnumerable<string> patterns) => string.Join(", ", patterns);

    /// <summary>What a set of patterns selects: the matching repository names and, for a typed package ID, whether it applies.</summary>
    public static async Task<PatternPreview> PreviewAsync(
        IRepositoryService repositories,
        string[] repositoryPatterns,
        string[] includePatterns,
        string[] excludePatterns,
        string? testPackage,
        CancellationToken cancellationToken)
    {
        var all = await repositories.ListAsync(cancellationToken);
        var names = all
            .Where(r => AccessPatterns.IsMatch(repositoryPatterns, r.Name))
            .Select(r => r.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var package = testPackage?.Trim();

        return new PatternPreview(
            names,
            string.IsNullOrEmpty(package) ? null : package,
            string.IsNullOrEmpty(package) ? null : AccessPatterns.Test(includePatterns, excludePatterns, package));
    }
}
