using System;

namespace BaGet.Identity.Application.Models.Users;

/// <summary>A user as the Users page lists them.</summary>
/// <param name="UserName">The user name.</param>
/// <param name="Email">The email address, if any.</param>
/// <param name="Created">When the account was created.</param>
/// <param name="IsAdministrator">Whether the user is an administrator.</param>
/// <param name="AvatarUrl">The avatar's address, or null for the initial.</param>
/// <param name="PendingApproval">Whether the account waits for an administrator to enable it (<c>Accounts:RequireApproval</c>).</param>
public record UserRow(string? UserName, string? Email, DateTime Created, bool IsAdministrator, string? AvatarUrl, bool PendingApproval = false);
