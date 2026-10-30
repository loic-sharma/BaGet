using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>The dashboard of one repository.</summary>
public sealed record ScanDashboardDto(ScanRepositoryDto Repository, ScanPolicy? OwnPolicy, AdminJobStatus? LastScan);
