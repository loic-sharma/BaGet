using System.ComponentModel.DataAnnotations;

namespace BaGet.Identity.Application.Models.Session;

/// <summary>The body of <c>POST /api/ui/session/exchange</c>.</summary>
public class SessionExchangeForm
{
    /// <summary>The <c>exchange_token</c> of the external sign-in's redirect.</summary>
    [Required]
    public string? ExchangeToken { get; set; }
}
