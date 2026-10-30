namespace BaGet.Web.V3.Models.Api;

/// <summary>Sets whether a user is an administrator.</summary>
public sealed class AdministratorRequest
{
    /// <summary>The new state.</summary>
    public bool? IsAdministrator { get; set; }
}
