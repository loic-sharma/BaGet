namespace BaGet.Core;

/// <summary>
/// Outgoing email. Optional: while <see cref="IsConfigured"/> is false, every feature
/// that needs email (password recovery, email confirmation) is switched off rather
/// than left half working, and an administrator recovers an account from the command line.
/// </summary>
[RestartRequired]
public class EmailOptions
{
    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>
    /// Set at startup when SMTP is configured but links in emails cannot be trusted (no
    /// "Site:PublicUrl" and unrestricted "AllowedHosts"): email is then treated as not configured.
    /// </summary>
    [HiddenSetting]
    public bool Suppressed { get; set; }

    public bool IsConfigured => !Suppressed && !string.IsNullOrEmpty(Smtp?.Host) && !string.IsNullOrEmpty(Smtp?.From);
}

public class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string? UserName { get; set; }

    [SensitiveSetting]
    public string? Password { get; set; }

    /// <summary>
    /// Use TLS: implicit on port 465, STARTTLS otherwise.
    /// </summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// The sender's address, optionally with a display name: "BaGet &lt;baget@example.com&gt;".
    /// </summary>
    public string? From { get; set; }
}
