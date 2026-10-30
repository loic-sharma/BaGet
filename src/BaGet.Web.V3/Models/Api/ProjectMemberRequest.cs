namespace BaGet.Web.V3.Models.Api;

/// <summary>Gives a user or group a role in a project.</summary>
public sealed class ProjectMemberRequest
{
    /// <summary><c>User</c> or <c>Group</c>.</summary>
    public string? Type { get; set; }

    /// <summary>The user's or group's name.</summary>
    public string? Principal { get; set; }

    /// <summary>The role: <c>Admin</c>, <c>Developer</c>, <c>Viewer</c>…</summary>
    public string? Role { get; set; }
}
