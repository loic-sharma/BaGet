using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Identity.Application.Models.Policies;
using BaGet.Identity.Application.UseCases.Access;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Frontend.Mvc.Screens;

/// <summary>
/// The thin forwards of the access screens' pattern helpers (the code is in <see cref="AccessPatternSupport"/>)
/// used by <see cref="AccessControllerBase"/>.
/// </summary>
internal static class AccessSupport
{
    /// <summary>The single actions, in display order.</summary>
    public static readonly AccessAction[] AllActions = AccessPatternSupport.AllActions;

    /// <summary>Parses the ticked action boxes (names of <see cref="AccessAction"/> values).</summary>
    public static AccessAction ParseActions(IEnumerable<string>? names) => AccessPatternSupport.ParseActions(names);

    /// <summary>The actions in <paramref name="actions"/> as words, or "none".</summary>
    public static string Words(AccessAction actions) => AccessPatternSupport.Words(actions);

    /// <summary>Patterns one per line, the way the editors show them.</summary>
    public static string Lines(IEnumerable<string> patterns) => AccessPatternSupport.Lines(patterns);

    /// <summary>The package filter and repository patterns as one line a person can read back.</summary>
    public static string Join(IEnumerable<string> patterns) => AccessPatternSupport.Join(patterns);

    /// <summary>What a set of patterns selects: the matching repository names and, for a typed package ID, whether it applies.</summary>
    public static Task<PatternPreview> PreviewAsync(
        IServiceProvider services,
        string[] repositoryPatterns,
        string[] includePatterns,
        string[] excludePatterns,
        string? testPackage,
        CancellationToken cancellationToken)
        => AccessPatternSupport.PreviewAsync(services.GetRequiredService<IRepositoryService>(), repositoryPatterns, includePatterns, excludePatterns, testPackage, cancellationToken);
}
