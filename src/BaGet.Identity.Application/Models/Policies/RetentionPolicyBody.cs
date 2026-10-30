namespace BaGet.Identity.Application.Models.Policies;

// The bodies of the retention, scanning and replication screens' writes. A page posts them as form fields (the view controllers bind the same
// names and build these), the JSON API takes them as the request body; the use cases never see how they arrived.

/// <summary>Creating or saving a retention policy, or any write of it that asks the password.</summary>
/// <param name="Form">The rules (<c>Form.*</c> on the page).</param>
/// <param name="Password">The administrator's password.</param>
public sealed record RetentionPolicyBody(RetentionPolicyForm? Form, string? Password);
