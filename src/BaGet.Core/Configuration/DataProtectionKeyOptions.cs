namespace BaGet.Core;

/// <summary>
/// Where the data protection key ring is kept (<c>DataProtection:*</c>). The ring encrypts remote repository passwords,
/// replication peer keys, sessions, antiforgery tokens, registry tokens and reset or confirmation links, so it must
/// outlive the process. Ignored when <c>Ha:Enabled</c> (the ring is then in the database).
/// </summary>
[RestartRequired]
public class DataProtectionKeyOptions
{
    /// <summary>
    /// The folder that holds the key ring. Default: <c>DataProtection-Keys</c> next to the settings overrides file
    /// (so inside the mounted volume when <c>SettingsOverridesPath</c> is). A relative path starts at the content root
    /// (or <c>BAGET_CONFIG_ROOT</c>). The keys are plain XML files: protect the folder like the database.
    /// </summary>
    public string? KeyPath { get; set; }
}
