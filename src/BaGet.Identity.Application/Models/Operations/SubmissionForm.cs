namespace BaGet.Identity.Application.Models.Operations;

/// <summary>A decision on a stuck submission of the validation queue.</summary>
/// <param name="SubmissionId">The submission.</param>
/// <param name="Password">The administrator's password (asked to release or reject).</param>
public sealed record SubmissionForm(string? SubmissionId, string? Password);
