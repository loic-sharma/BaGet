using System.ComponentModel.DataAnnotations;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>What the authenticator-code form posts.</summary>
public class TwoFactorLoginForm
{
    /// <summary>The code from the authenticator app.</summary>
    [Required]
    [Display(Name = "Authenticator code")]
    public string? Code { get; set; }

    /// <summary>Whether the session outlives the browser.</summary>
    public bool RememberMe { get; set; }

    /// <summary>Where to go after signing in; only a local URL is followed.</summary>
    public string? ReturnUrl { get; set; }
}
