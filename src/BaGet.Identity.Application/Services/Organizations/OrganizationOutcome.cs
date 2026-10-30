namespace BaGet.Identity.Application.Services.Organizations;

/// <summary>What happens to one organization when an administrator deletes a member.</summary>
/// <param name="Name">The organization.</param>
/// <param name="Outcome">A sentence for the confirmation page.</param>
public record OrganizationOutcome(string? Name, string Outcome);
