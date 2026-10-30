using System.ComponentModel.DataAnnotations;

namespace BaGet.Identity.Application.Models.Session;

/// <summary>The body of <c>POST /api/ui/session/refresh</c>.</summary>
public class SessionRefreshForm
{
    /// <summary>The <c>refreshToken</c> of the last answer.</summary>
    [Required]
    public string? RefreshToken { get; set; }
}
