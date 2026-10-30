using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>One repository of the scanning overview.</summary>
/// <param name="Repository">The repository.</param>
/// <param name="Summary">Its scan counts.</param>
/// <param name="Policy">The policy in force.</param>
public sealed record ScanningRepositoryRow(RepositoryInfo Repository, ScanSummary Summary, EffectiveScanPolicy Policy);
