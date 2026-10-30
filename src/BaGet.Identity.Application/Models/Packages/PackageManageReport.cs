namespace BaGet.Identity.Application.Models.Packages;

/// <summary>An open abuse report.</summary>
/// <param name="Key">The report's key (what <c>ResolveReport</c> takes).</param>
/// <param name="PackageVersion">The reported version.</param>
/// <param name="Reason">The reason given.</param>
/// <param name="Details">The details given.</param>
/// <param name="ReporterEmail">Where to reply, if the reporter gave an address.</param>
public sealed record PackageManageReport(int Key, string? PackageVersion, string? Reason, string? Details, string? ReporterEmail);
