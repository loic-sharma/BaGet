using System;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>The scanning options the overview shows.</summary>
/// <param name="Enabled">Whether artifacts are scanned.</param>
/// <param name="ScanOnDeploy">Whether a stored version is queued for a scan at once.</param>
/// <param name="OsvUrl">The OSV API root.</param>
/// <param name="OsvOfflinePath">The offline copy of the OSV data, or null.</param>
/// <param name="RescanInterval">The age after which an artifact is scanned again (zero or negative: never).</param>
public sealed record ScanningSettingsView(bool Enabled, bool ScanOnDeploy, string OsvUrl, string? OsvOfflinePath, TimeSpan RescanInterval);
