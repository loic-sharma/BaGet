using System.ComponentModel.DataAnnotations;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>What the recovery-code form posts.</summary>
public class RecoveryCodeLoginForm
{
    /// <summary>The recovery code.</summary>
    [Required]
    [Display(Name = "Recovery code")]
    public string? RecoveryCode { get; set; }

    /// <summary>Where to go after signing in; only a local URL is followed.</summary>
    public string? ReturnUrl { get; set; }
}
