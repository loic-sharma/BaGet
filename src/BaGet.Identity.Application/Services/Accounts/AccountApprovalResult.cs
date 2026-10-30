namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>The outcome of <see cref="AccountApprovalService.ApproveAsync"/>.</summary>
public enum AccountApprovalResult
{
    /// <summary>The account is enabled and can sign in.</summary>
    Approved,

    /// <summary>The account was not waiting for approval.</summary>
    NotPending,

    /// <summary>There is no such user.</summary>
    UserNotFound,

    /// <summary>The account changed or was deleted while it was being approved; nothing was approved.</summary>
    Conflict,
}
