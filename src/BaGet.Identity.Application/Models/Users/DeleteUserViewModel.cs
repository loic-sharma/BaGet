using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Identity.Application.Models.Users;

/// <summary>What the delete-user screen (<c>/admin/users/{name}/delete</c>) shows: what the deletion would remove or change.</summary>
public sealed class DeleteUserViewModel : ConfirmationModel
{
    /// <summary>Shown when permanent package deletion is asked for in read-only mode.</summary>
    public const string ReadOnlyMessage = "The server is read-only, so packages cannot be deleted. Leave them without owners, or turn read-only mode off.";

    /// <summary>The user to delete (route).</summary>
    public string? Name { get; set; }

    /// <summary>"keep" (default) or "delete": what to do with the IDs only this user owns.</summary>
    public string SoleOwned { get; set; } = "keep";

    /// <summary>What the deletion would remove or change.</summary>
    public AdminDeletionPreview? Preview { get; internal set; }

    /// <summary>Whether the user is the server's only administrator.</summary>
    public bool LastAdministrator { get; internal set; }

    /// <summary>Read-only mode: the account may go, but package files are not deleted.</summary>
    public bool IsReadOnlyMode { get; internal set; }

    /// <summary>Why the deletion was refused.</summary>
    public string? Error { get; internal set; }
}
