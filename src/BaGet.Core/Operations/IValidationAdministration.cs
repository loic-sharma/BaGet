using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>Where a submission stands: still to run, or stopped without being indexed.</summary>
public enum ValidationSubmissionState
{
    Pending,

    /// <summary>A validator failed or indexing failed; the package stays staged for an administrator to decide.</summary>
    Quarantined,
}

/// <summary>One submission awaiting validation or quarantined.</summary>
public sealed record ValidationSubmissionSummary(
    string SubmissionId,
    string Repository,
    string? PackageId,
    string? PackageVersion,
    ValidationSubmissionState State,
    DateTime Created,
    IReadOnlyList<string> FailureMessages);

/// <summary>
/// The administrator's view of the validation queue across every repository, and the three decisions on a stuck
/// submission. Every write throws <see cref="MaintenanceException"/> while the gate is closed.
/// </summary>
public interface IValidationAdministration
{
    /// <summary>Whether validation can be administered (a relational database; false on Azure Table).</summary>
    bool IsSupported { get; }

    /// <summary>Pending and quarantined submissions, oldest first.</summary>
    Task<IReadOnlyList<ValidationSubmissionSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Resets a submission's steps to pending and queues it again.</summary>
    /// <returns>Null on success, else why not.</returns>
    Task<string?> RetryAsync(string submissionId, string? actor, CancellationToken cancellationToken);

    /// <summary>
    /// Indexes a quarantined package although validation failed. Audited. The failed steps keep their verdicts.
    /// </summary>
    /// <returns>Null on success, else why not.</returns>
    Task<string?> ReleaseAsync(string submissionId, string? actor, CancellationToken cancellationToken);

    /// <summary>Deletes the staged file and the submission's rows. Audited. A pending submission can be rejected too (one that keeps failing would never leave the queue).</summary>
    /// <returns>Null on success, else why not.</returns>
    Task<string?> RejectAsync(string submissionId, string? actor, CancellationToken cancellationToken);
}
