using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What the policy form partial needs.</summary>
/// <param name="Fields">The current values.</param>
/// <param name="Confirmation">The password confirmation input.</param>
/// <param name="Idprefix">A prefix for the input ids on the page.</param>
public sealed record ScanPolicyFormView(ScanPolicyFields Fields, ConfirmPasswordView Confirmation, string Idprefix);
