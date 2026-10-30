namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>How an administrator-created account gets its first password.</summary>
public enum NewUserPasswordMode
{
    /// <summary>The administrator typed one.</summary>
    Given,

    /// <summary>The server generates one, shown to the administrator once.</summary>
    Generated,

    /// <summary>
    /// The account gets an unknown random password and is mailed a reset link to choose its own. Needs an
    /// email address and a configured SMTP server; the caller checks that and queues the mail.
    /// </summary>
    Invited,
}
