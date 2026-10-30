using System;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>The account an administrator looks at, without anything that signs in as it (no password hash, security stamp or tokens).</summary>
/// <param name="UserName">The user name.</param>
/// <param name="Email">The email address, or null.</param>
/// <param name="Created">When the account was created.</param>
/// <param name="IsAdministrator">Whether it is a server administrator.</param>
/// <param name="PendingApproval">Whether it waits for an administrator to enable it.</param>
public sealed record UserAccountInfo(string? UserName, string? Email, DateTime Created, bool IsAdministrator, bool PendingApproval);
