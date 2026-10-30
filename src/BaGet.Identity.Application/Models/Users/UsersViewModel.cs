using System;
using System.Collections.Generic;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.Models.Users;

/// <summary>What the Users screen (<c>/admin/users</c>) shows: the filtered, paged accounts and the state of the last action.</summary>
public sealed class UsersViewModel : ConfirmationModel, IFlashModel
{
    /// <summary>How many users a page lists.</summary>
    public const int PageSize = 50;

    /// <summary>User-name filter (contains).</summary>
    public string? Q { get; set; }

    /// <summary>One-based page number (the <c>p</c> query value; not "page", which Razor Pages reserved for the page path).</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>The users on this page.</summary>
    public IReadOnlyList<UserRow> Users { get; internal set; } = Array.Empty<UserRow>();

    /// <summary>How many accounts, in all pages, wait for approval.</summary>
    public int PendingCount { get; internal set; }

    /// <summary>The number of pages for the current filter.</summary>
    public int PageCount { get; internal set; } = 1;

    /// <summary>The signed in administrator's user name; their own row has no actions.</summary>
    public string? CurrentUserName { get; internal set; }

    /// <summary>Whether a reset link can be mailed: SMTP is configured.</summary>
    public bool CanEmailResetLink { get; internal set; }

    /// <summary>Whether an invitation (a reset link mailed to the new user) can be offered: SMTP is configured.</summary>
    public bool CanInvite { get; internal set; }

    /// <summary>
    /// The password just generated. Only ever rendered by the response to the reset itself, never kept in TempData or a cookie.
    /// </summary>
    public string? GeneratedPassword { get; internal set; }

    /// <summary>The user <see cref="GeneratedPassword"/> belongs to.</summary>
    public string? GeneratedFor { get; internal set; }

    /// <summary>Whether <see cref="GeneratedPassword"/> is a new account's first password rather than a reset.</summary>
    public bool GeneratedForNewUser { get; internal set; }

    /// <inheritdoc />
    public string? Message { get; set; }

    /// <inheritdoc />
    public string? Error { get; set; }

    /// <summary>Whether <paramref name="row"/> is another account, so actions apply to it.</summary>
    public bool CanAct(UserRow row)
        => !string.Equals(row.UserName, CurrentUserName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The address with all but its first letter and domain hidden, for a confirmation.</summary>
    public static string MaskEmail(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        if (at < 1) return "the user's address";

        #pragma warning disable CS8602
        return email.Substring(0, 1) + "***" + email.Substring(at);
        #pragma warning restore CS8602
    }
}
