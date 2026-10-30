using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BaGet.Validation.Pipeline;
using BaGet.Validation.Queue;

namespace BaGet.Validation.Services;

/// <summary>
/// Lists the pending and quarantined submissions of every repository, and retries, releases or rejects one.
/// Releasing indexes a package that failed validation, so it is logged as a warning and recorded with
/// <see cref="IAdminAudit"/> (the seam for the access-audit table).
/// </summary>
public sealed class ValidationAdministration : IValidationAdministration
{
    private readonly IContext _context;
    private readonly IRepositoryService _repositories;
    private readonly IRepositoryContext _repository;
    private readonly IStorageService _storage;
    private readonly IValidationQueue _queue;
    private readonly PackageValidationRunner _runner;
    private readonly IAdminAudit _audit;
    private readonly ILogger<ValidationAdministration> _logger;
    private readonly MaintenanceGate? _gate;

    public ValidationAdministration(
        IContext context,
        IRepositoryService repositories,
        IRepositoryContext repository,
        IStorageService storage,
        IValidationQueue queue,
        PackageValidationRunner runner,
        IAdminAudit audit,
        ILogger<ValidationAdministration> logger,
        MaintenanceGate? gate = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gate = gate;
    }

    /// <inheritdoc />
    public bool IsSupported => _context is not NullContext && _context.PackageValidations != null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationSubmissionSummary>> ListAsync(CancellationToken cancellationToken)
    {
        if (!IsSupported) return Array.Empty<ValidationSubmissionSummary>();

        #pragma warning disable CS8604, CS8619
        var open = await _context.PackageValidations!
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.Type == PackageValidation.IndexType && v.State != PackageValidationState.Succeeded)
            .OrderBy(v => v.Created).ThenBy(v => v.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (open.Count == 0) return Array.Empty<ValidationSubmissionSummary>();

        var ids = open.Select(v => v.SubmissionId).ToList();
        var failed = (await _context.PackageValidations!
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(v => ids.Contains(v.SubmissionId) && v.State == PackageValidationState.Failed)
                .OrderBy(v => v.Key)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToLookup(v => v.SubmissionId);
        #pragma warning restore CS8604, CS8619

        var names = (await _repositories.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(r => r.Key, r => r.Name);

        return open
            .Select(index => new ValidationSubmissionSummary(
                index.SubmissionId!,
                names.TryGetValue(index.RepositoryKey, out var name) ? name : Repository.DefaultName,
                index.PackageId,
                index.PackageVersion,
                index.State == PackageValidationState.Pending ? ValidationSubmissionState.Pending : ValidationSubmissionState.Quarantined,
                index.Created,
                failed[index.SubmissionId]
                    .Where(v => !string.IsNullOrEmpty(v.Message))
                    .Select(v => v.Type == PackageValidation.IndexType ? v.Message! : $"{v.Type}: {v.Message}")
                    .ToList()))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<string?> RetryAsync(string submissionId, string? actor, CancellationToken cancellationToken)
    {
        _gate?.ThrowIfClosed();

        var (steps, error) = await LoadAsync(submissionId, cancellationToken);
        if (error != null) return error;

        var index = steps.Single(s => s.Type == PackageValidation.IndexType);
        if (!await StagedExistsAsync(submissionId, cancellationToken)) return "The staged package is missing, so it cannot be validated again.";

        foreach (var step in steps)
        {
            step.State = PackageValidationState.Pending;
            step.Message = null;
            step.Completed = null;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _queue.EnqueueAsync(submissionId, cancellationToken).ConfigureAwait(false);

        _audit.Record(actor, "validation.retry", Describe(index));

        return null;
    }

    /// <inheritdoc />
    public async Task<string?> ReleaseAsync(string submissionId, string? actor, CancellationToken cancellationToken)
    {
        _gate?.ThrowIfClosed();

        var (steps, error) = await LoadAsync(submissionId, cancellationToken);
        if (error != null) return error;

        var index = steps.Single(s => s.Type == PackageValidation.IndexType);
        if (index.State != PackageValidationState.Failed) return "Only a quarantined submission can be released; a pending one is still being validated.";

        var failedSteps = string.Join("; ", steps.Where(s => s.State == PackageValidationState.Failed && s != index).Select(s => $"{s.Type}: {s.Message}"));
        var releaseError = await _runner.ReleaseAsync(submissionId, actor, cancellationToken).ConfigureAwait(false);
        if (releaseError != null) return releaseError;

        _logger.LogWarning(
            "{Actor} released quarantined package {Package} from submission {SubmissionId}; validation had failed: {Failures}",
            actor, Describe(index), submissionId, failedSteps);
        _audit.Record(actor, "validation.release", Describe(index), failedSteps.Length == 0 ? "indexing had failed" : failedSteps);

        return null;
    }

    /// <inheritdoc />
    public async Task<string?> RejectAsync(string submissionId, string? actor, CancellationToken cancellationToken)
    {
        _gate?.ThrowIfClosed();

        // A validator may be running on it: stop that first, or it would publish what is being rejected.
        if (!await PackageValidationRunner.StopRunAsync(submissionId, cancellationToken).ConfigureAwait(false))
        {
            return "That submission is being validated and did not stop in time; try again.";
        }

        var (steps, error) = await LoadAsync(submissionId, cancellationToken);
        if (error != null) return error;

        var index = steps.Single(s => s.Type == PackageValidation.IndexType);
        // A pending submission can be rejected too: one whose validation keeps failing would otherwise stay pending for
        // good (pushing the version again answers 409, the repository cannot be deleted). The runner skips a submission
        // whose rows are gone.
        if (index.State != PackageValidationState.Failed && index.State != PackageValidationState.Pending) return "Only a pending or quarantined submission can be rejected.";

        // File first: if the rows could not be removed the submission stays listed and can be rejected again.
        await _storage.DeleteAsync(ValidatingPackageSubmissionService.StagedPath(_repository.Current, submissionId), cancellationToken).ConfigureAwait(false);
        _context.PackageValidations!.RemoveRange(steps);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _audit.Record(actor, "validation.reject", Describe(index));

        return null;
    }

    private string Describe(PackageValidation index)
        => $"repository {_repository.Current.Name}, {index.PackageId} {index.PackageVersion} (submission {index.SubmissionId})";

    /// <summary>Loads the submission's steps and switches to its repository.</summary>
    private async Task<(List<PackageValidation> Steps, string? Error)> LoadAsync(string submissionId, CancellationToken cancellationToken)
    {
        if (!IsSupported) return (new List<PackageValidation>(), "Validation is not available with this database.");

        #pragma warning disable CS8604
        var steps = await _context.PackageValidations!
            .IgnoreQueryFilters()
            .Where(v => v.SubmissionId == submissionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        #pragma warning restore CS8604

        var index = steps.SingleOrDefault(s => s.Type == PackageValidation.IndexType);
        if (index == null) return (steps, "There is no such submission.");
        if (index.State == PackageValidationState.Succeeded) return (steps, "That package was already indexed.");

        if (!await _runner.UseSubmissionRepositoryAsync(steps, submissionId, cancellationToken).ConfigureAwait(false))
        {
            return (steps, "The submission's repository no longer exists.");
        }

        return (steps, null);
    }

    private async Task<bool> StagedExistsAsync(string submissionId, CancellationToken cancellationToken)
    {
        try
        {
            using var staged = await _storage.GetAsync(ValidatingPackageSubmissionService.StagedPath(_repository.Current, submissionId), cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception e) when (!(e is OperationCanceledException))
        {
            return false;
        }
    }
}
