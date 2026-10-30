using BaGet.Core;

namespace BaGet.Identity.Application.Screens;

/// <summary>Who may use an administration screen.</summary>
public enum GateRole
{
    /// <summary>Anybody the other requirements let in.</summary>
    Anyone,

    /// <summary>Server administrators.</summary>
    Administrator,

    /// <summary>
    /// The access screens: server administrators, or whoever <see cref="GateRequest.AlsoAllow"/> admits (a project's administrators; a key only when it carries <see cref="ApiKeyScopes.Admin"/> and no repository limit);
    /// 503 on a write while the server is read-only; an administrator is sent to the "unavailable" screen where there is no access model.
    /// </summary>
    AccessAdministrator,
}
