using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>What the new-password form behind a reset link posts.</summary>
public class ResetPasswordForm
{
    /// <summary>The account named by the link.</summary>
    public string? UserId { get; set; }

    /// <summary>The encoded reset token of the link.</summary>
    public string? Code { get; set; }

    /// <summary>The new password. Never echoed.</summary>
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string? Password { get; set; }

    /// <summary>The new password again. Never echoed.</summary>
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string? ConfirmPassword { get; set; }

    /// <summary>Whether the link carries a user and a token.</summary>
    [JsonIgnore]
    public bool LinkIsUsable => !string.IsNullOrEmpty(UserId) && !string.IsNullOrEmpty(Code);
}
