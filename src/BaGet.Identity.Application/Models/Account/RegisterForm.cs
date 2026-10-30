using System.ComponentModel.DataAnnotations;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>What the registration form posts.</summary>
public class RegisterForm
{
    /// <summary>The wanted user name.</summary>
    [Required]
    [Display(Name = "User name")]
    public string? UserName { get; set; }

    /// <summary>The email address.</summary>
    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>The password. Never echoed.</summary>
    [Required]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    /// <summary>The password again. Never echoed.</summary>
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string? ConfirmPassword { get; set; }

    /// <summary>Where to go after signing in; only a local URL is followed.</summary>
    public string? ReturnUrl { get; set; }
}
