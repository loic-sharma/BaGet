namespace BaGet.Identity.Models.Packages;

/// <summary>The form of the Maven management handlers: the chosen version and what the handler needs.</summary>
public sealed class MavenManageForm
{
    /// <summary>The user or organization (<c>AddOwner</c>, <c>RemoveOwner</c>).</summary>
    public string? OwnerName { get; set; }

    /// <summary>The version whose files are listed, and the one a handler acts on (<c>DeleteFile</c>, <c>DeleteVersion</c>, <c>Cleanup</c>).</summary>
    public string? Version { get; set; }

    /// <summary>The file to delete (<c>DeleteFile</c>).</summary>
    public string? FileName { get; set; }

    /// <summary>How many snapshot builds to keep (<c>Cleanup</c>).</summary>
    public string? Keep { get; set; }

    /// <summary>The caller's password, for the deleting handlers.</summary>
    public string? Password { get; set; }
}
