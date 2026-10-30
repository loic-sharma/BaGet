namespace BaGet.Identity.Application.Models.Policies;

/// <summary>Saving the scanning policy of a repository or project (empty values remove it).</summary>
/// <param name="Warn">Warn from this severity (0 to 3, or empty).</param>
/// <param name="BlockDownload">Refuse downloads from this severity.</param>
/// <param name="BlockDeploy">Refuse deploys from this severity.</param>
/// <param name="BlockUnscanned">Refuse downloads of versions that were not scanned.</param>
/// <param name="Password">The administrator's password.</param>
public sealed record ScanPolicyBody(string? Warn, string? BlockDownload, string? BlockDeploy, bool BlockUnscanned, string? Password)
{
    /// <summary>The fields the policy editor edits.</summary>
    public ScanPolicyFields ToFields() => new() { Warn = Warn, BlockDownload = BlockDownload, BlockDeploy = BlockDeploy, BlockUnscanned = BlockUnscanned };
}
