using System.ComponentModel.DataAnnotations;

namespace BaGet.Identity.Application.Models.Session;

/// <summary>The body of <c>POST /api/ui/session/two-factor</c>: the ticket of the password step and a code of the authenticator app.</summary>
public class SessionTwoFactorForm
{
    /// <summary>The <c>twoFactorTicket</c> the password step answered.</summary>
    [Required]
    public string? Ticket { get; set; }

    /// <summary>The code from the authenticator app.</summary>
    [Required]
    public string? Code { get; set; }
}
