using System.ComponentModel.DataAnnotations;

namespace BaGet.Identity.Application.Models.Session;

/// <summary>The body of <c>POST /api/ui/session/recovery-code</c>: the ticket of the password step and one of the account's recovery codes (each works once).</summary>
public class SessionRecoveryCodeForm
{
    /// <summary>The <c>twoFactorTicket</c> the password step answered.</summary>
    [Required]
    public string? Ticket { get; set; }

    /// <summary>A recovery code.</summary>
    [Required]
    public string? RecoveryCode { get; set; }
}
