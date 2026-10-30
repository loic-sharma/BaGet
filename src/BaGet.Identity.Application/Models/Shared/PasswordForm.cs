namespace BaGet.Identity.Application.Models.Shared;

// The bodies of the administration screens' writes. A page posts them as form fields (the view controllers bind the same names and build these),
// the JSON API takes them as the request body; the use cases never see how they arrived.

/// <summary>A write that only asks the administrator's password again (approve an account, delete an organization).</summary>
/// <param name="Password">The administrator's password.</param>
public sealed record PasswordForm(string? Password);
