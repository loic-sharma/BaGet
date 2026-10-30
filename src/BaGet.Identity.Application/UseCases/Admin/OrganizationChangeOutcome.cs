namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>What an organization change decided: the (possibly new) name of the organization and the message or error to show.</summary>
internal sealed class OrganizationChangeOutcome
{
    public string? Name { get; set; }

    public string? Message { get; set; }

    public string? Error { get; set; }
}
