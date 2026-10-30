using System.ComponentModel.DataAnnotations;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>What the sign-in form posts (and the body of <c>POST /api/ui/account/login</c>).</summary>
public class LoginForm
{
    /// <summary>The user name.</summary>
    [Required]
    [Display(Name = "User name")]
    public string? UserName { get; set; }

    /// <summary>The password. Never echoed: a screen's model leaves it empty.</summary>
    [Required]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    /// <summary>Whether the session outlives the browser.</summary>
    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    /// <summary>Where to go after signing in; only a local URL is followed.</summary>
    public string? ReturnUrl { get; set; }
}
