using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>One project of the scanning overview.</summary>
/// <param name="Project">The project.</param>
/// <param name="HasPolicy">Whether it has a scanning policy of its own.</param>
public sealed record ScanningProjectRow(ProjectSummary Project, bool HasPolicy);
