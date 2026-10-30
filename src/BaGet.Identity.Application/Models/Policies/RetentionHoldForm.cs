namespace BaGet.Identity.Application.Models.Policies;

/// <summary>Placing a legal hold.</summary>
/// <param name="HoldRepository">The repository of the hold.</param>
/// <param name="HoldName">The artifact name.</param>
/// <param name="HoldVersion">The version; empty for every version.</param>
/// <param name="Reason">Why (a case number, a ticket).</param>
public sealed record RetentionHoldForm(string? HoldRepository, string? HoldName, string? HoldVersion, string? Reason);
