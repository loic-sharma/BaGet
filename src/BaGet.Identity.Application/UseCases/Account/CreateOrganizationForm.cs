namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>What the "create an organization" form posts.</summary>
public sealed class CreateOrganizationForm
{
    /// <summary>The organization's name.</summary>
    public string? Name { get; set; }
}
