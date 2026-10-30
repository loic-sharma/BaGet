using System.Collections.Generic;

namespace BaGet.Web.V3.Models.Api;

/// <summary>The scanning setup and every repository's numbers.</summary>
public sealed record ScanOverviewDto(bool Enabled, bool TrivyConfigured, IReadOnlyList<ScanRepositoryDto> Repositories);
