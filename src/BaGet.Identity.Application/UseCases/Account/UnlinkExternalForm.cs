namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>What unlinking an external provider posts.</summary>
public sealed class UnlinkExternalForm
{
    /// <summary>The id of the provider to unlink.</summary>
    public string? Provider { get; set; }

    /// <summary>The password, asked again.</summary>
    public string? Password { get; set; }
}
