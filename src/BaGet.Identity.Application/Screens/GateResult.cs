namespace BaGet.Identity.Application.Screens;

/// <summary>The gate's decision and what it learned on the way.</summary>
/// <param name="Outcome">Whether the screen may run, and if not why.</param>
/// <param name="IsServerAdministrator">Whether the caller is a server administrator (only evaluated for the roles that need it).</param>
public readonly record struct GateResult(GateOutcome Outcome, bool IsServerAdministrator)
{
    /// <summary>Why the screen was refused, in words for an API client (null when there is nothing to add).</summary>
    public string? Message { get; init; }

    /// <summary>Whether the screen may run.</summary>
    public bool IsAllowed => Outcome == GateOutcome.Allowed;
}
