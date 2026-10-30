using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;

namespace BaGet.Identity.Application.UseCases.Access;

/// <summary>
/// Who may open an access screen beyond the server administrators: the administrators of the project the screen acts on. One place for the view
/// controllers' and the JSON twins' gates.
/// </summary>
public static class AccessAuthorization
{
    /// <summary>The projects list: server administrators, and anyone who administers at least one project.</summary>
    public static async Task<bool> ProjectsAsync(IAccessAdministration access, bool isServerAdministrator, string? userId, CancellationToken cancellationToken)
        => isServerAdministrator || (await AdministeredAsync(access, userId, cancellationToken)).Count > 0;

    /// <summary>A project screen: server administrators, and the project's own administrators (the project is the <c>name</c> route value).</summary>
    public static async Task<bool> ProjectAsync(IAccessAdministration access, bool isServerAdministrator, string? userId, string? projectName, CancellationToken cancellationToken)
    {
        if (isServerAdministrator) return true;

        return !string.IsNullOrEmpty(projectName) && userId != null && await access.IsProjectAdminAsync(userId, projectName, cancellationToken);
    }

    /// <summary>A permission target screen: server administrators, and the administrators of the target's project for a project target.</summary>
    public static async Task<bool> TargetAsync(IAccessAdministration access, bool isServerAdministrator, string? userId, string? targetName, CancellationToken cancellationToken)
    {
        if (isServerAdministrator) return true;
        if (string.IsNullOrEmpty(targetName) || userId == null) return false;

        var target = await access.GetTargetAsync(targetName, cancellationToken);

        return target?.Target.ProjectName != null && await access.IsProjectAdminAsync(userId, target.Target.ProjectName, cancellationToken);
    }

    /// <summary>The names of the projects the user administers.</summary>
    public static async Task<IReadOnlyList<string>> AdministeredAsync(IAccessAdministration access, string? userId, CancellationToken cancellationToken)
        => userId == null
            ? Array.Empty<string>()
            : (await access.ProjectRolesOfUserAsync(userId, cancellationToken))
                .Where(r => r.Role == ProjectRole.Admin).Select(r => r.Project).Distinct(StringComparer.Ordinal).ToList();
}
