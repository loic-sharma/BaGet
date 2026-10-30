namespace BaGet.Web.V3.Models.Api;

/// <summary>Creates an account.</summary>
public sealed class CreateUserRequest
{
    /// <summary>The user name.</summary>
    public string? UserName { get; set; }

    /// <summary>The address (trusted: marked confirmed), or none.</summary>
    public string? Email { get; set; }

    /// <summary>The first password; none = the server generates one and returns it once.</summary>
    public string? Password { get; set; }
}
