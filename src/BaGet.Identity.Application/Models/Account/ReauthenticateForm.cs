namespace BaGet.Identity.Application.Models.Account;

/// <summary>What the "sign in again" button posts.</summary>
public class ReauthenticateForm
{
    /// <summary>Where to go after the provider answered; only a local URL is followed.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>The id of the linked provider to sign in again with; empty takes the only linked one (several: the screen offers the choice).</summary>
    public string? Provider { get; set; }
}
