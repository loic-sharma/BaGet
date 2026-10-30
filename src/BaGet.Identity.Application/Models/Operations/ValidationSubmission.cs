using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Operations;

/// <summary>One version's newest validation submission and the verdict of every step.</summary>
public sealed record ValidationSubmission(string Version, string? SubmissionId, string Status, IReadOnlyList<PackageValidation> Steps);
