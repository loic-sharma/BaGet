namespace BaGet.Identity.Application.Screens;

/// <summary>The answer of the <see cref="ScreenGate"/>.</summary>
public enum GateOutcome
{
    /// <summary>The caller may use the screen.</summary>
    Allowed,

    /// <summary>Accounts are disabled: there is nothing to show (404).</summary>
    AccountsOff,

    /// <summary>The screen does not exist for this caller (404): no mail configured, not an administrator, not a project administrator.</summary>
    NotFound,

    /// <summary>The caller must sign in.</summary>
    SignIn,

    /// <summary>The caller is known but this credential may not do it (an interactive-only twin over HTTP for a key or an anonymous caller, a key without the administration scope).</summary>
    Forbidden,

    /// <summary>The database has no access model (Azure Table) and the caller is an administrator: the screen says so.</summary>
    AccessUnavailable,
}
