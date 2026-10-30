using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A repository with its scan numbers and the policy in force.</summary>
public sealed record ScanRepositoryDto(string Repository, ScanSummary Summary, EffectiveScanPolicy Policy);
