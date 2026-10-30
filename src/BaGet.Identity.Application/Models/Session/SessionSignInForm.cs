using System.ComponentModel.DataAnnotations;

namespace BaGet.Identity.Application.Models.Session;

/// <summary>The body of <c>POST /api/ui/session</c>: the same credentials as the sign-in page.</summary>
public class SessionSignInForm
{
    /// <summary>The user name.</summary>
    [Required]
    public string? UserName { get; set; }

    /// <summary>The password.</summary>
    [Required]
    [DataType(DataType.Password)]
    public string? Password { get; set; }
}
