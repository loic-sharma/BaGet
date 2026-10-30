using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.Models.Operations;

/// <summary>What the validation queue screen (<c>/admin/validation</c>) shows: pending and quarantined submissions.</summary>
public sealed class ValidationViewModel : ConfirmationModel, IFlashModel
{
    /// <summary>Whether validation can be administered here.</summary>
    public bool IsSupported { get; internal set; }

    /// <summary>The pending and quarantined submissions.</summary>
    public IReadOnlyList<ValidationSubmissionSummary> Submissions { get; internal set; } = Array.Empty<ValidationSubmissionSummary>();

    /// <inheritdoc />
    public string? Message { get; set; }

    /// <inheritdoc />
    public string? Error { get; set; }
}
