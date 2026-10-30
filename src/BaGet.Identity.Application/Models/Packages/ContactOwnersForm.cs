namespace BaGet.Identity.Application.Models.Packages;

/// <summary>
/// The fields of the contact-owners form as the screen receives them (a page's form post or the JSON body of the twin). It carries no
/// validation attributes: the use case validates by itself (<see cref="ContactOwnersViewModel"/> holds the rules), so a page's model
/// binding does not report the same error a second time.
/// </summary>
public sealed class ContactOwnersForm
{
    /// <summary>The message to send to the owners.</summary>
    public string? Message { get; set; }

    /// <summary>Whether the sender also gets a copy.</summary>
    public bool CopyToSelf { get; set; }
}
