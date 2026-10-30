using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Validation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using BaGet.Validation.Signing;

namespace BaGet.Validation.Pipeline;

/// <summary>
/// Runs one submission: every validator, then, if all passed, repository signing and
/// indexing. A package that fails stays staged, quarantined, for its owners to inspect.
/// </summary>
public class PackageValidationRunner
{
    private readonly IContext _context;
    private readonly IStorageService _storage;
    private readonly IPackageIndexingService _indexer;
    private readonly IPackageDatabase _packages;
    private readonly IEnumerable<IPackageValidator> _validators;
    private readonly RepositorySigner _signer;
    private readonly SystemTime _time;
    private readonly IOptionsSnapshot<ValidationOptions> _options;
    private readonly ILogger<PackageValidationRunner> _logger;
    private readonly IRepositoryContext _repository;
    private readonly IRepositoryService _repositories;

    // Submissions being validated by this process: a reject cancels the run and waits for it to end.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ActiveRun> Running = new();

    /// <summary>
    /// Stops this process's validation of the submission, if any, and waits (bounded) until it has ended, so nothing is
    /// published after the caller removes the submission. False when it did not end in time.
    /// </summary>
    internal static async Task<bool> StopRunAsync(string submissionId, CancellationToken cancellationToken)
    {
        if (!Running.TryGetValue(submissionId, out var run)) return true;

        run.Cancel.Cancel();
        var finished = await Task.WhenAny(run.Finished.Task, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken));

        return finished == run.Finished.Task;
    }

    public PackageValidationRunner(
        IContext context,
        IStorageService storage,
        IPackageIndexingService indexer,
        IPackageDatabase packages,
        IEnumerable<IPackageValidator> validators,
        RepositorySigner signer,
        SystemTime time,
        IOptionsSnapshot<ValidationOptions> options,
        ILogger<PackageValidationRunner> logger,
        IRepositoryContext repository,
        IRepositoryService repositories)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>?> GetPendingSubmissionIdsAsync(CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604, CS8619
        // Every repository's: the worker is not tied to one, RunAsync switches to the submission's.
        return await _context.PackageValidations
            .IgnoreQueryFilters()
            .Where(v => v.State == PackageValidationState.Pending)
            .Select(v => v.SubmissionId)
            .Distinct()
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8604, CS8619
    }

    public async Task RunAsync(string submissionId, CancellationToken cancellationToken)
    {
        var run = new ActiveRun();
        Running[submissionId] = run;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, run.Cancel.Token);
            try
            {
                await RunTrackedAsync(submissionId, linked.Token);
            }
            catch (OperationCanceledException) when (run.Cancel.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Validation of submission {SubmissionId} was stopped because it is being rejected", submissionId);
            }
        }
        finally
        {
            Running.TryRemove(submissionId, out _);
            run.Finished.TrySetResult(true);
            run.Cancel.Dispose();
        }
    }

    private async Task RunTrackedAsync(string submissionId, CancellationToken cancellationToken)
    {
        // Costs nothing without a listener. A submission with nothing pending (already finished) is not a validation.
        using var activity = BaGetDiagnostics.Source.StartActivity(BaGetDiagnostics.Spans.PackageValidate);

        try
        {
            var result = await RunCoreAsync(submissionId, activity, cancellationToken);
            if (result == null)
            {
                activity?.SetTag(BaGetDiagnostics.Tags.Result, "Skipped");
                return;
            }

            activity?.SetTag(BaGetDiagnostics.Tags.Result, result);
            BaGetDiagnostics.ValidationResults.Add(1, new KeyValuePair<string, object?>(BaGetDiagnostics.Tags.Result, result));
        }
        catch (Exception e)
        {
            BaGetDiagnostics.Fail(activity, e);
            BaGetDiagnostics.ValidationResults.Add(1, new KeyValuePair<string, object?>(BaGetDiagnostics.Tags.Result, "error"));

            throw;
        }
    }

    /// <returns>"Succeeded" or "Failed" (quarantined), or null when there was nothing to run.</returns>
    private async Task<string?> RunCoreAsync(string submissionId, Activity? activity, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        // A queued submission names no repository: find it in whichever one it belongs to, then work there, so
        // the indexing below lands in that repository.
        var steps = await _context.PackageValidations
            .IgnoreQueryFilters()
            .Where(v => v.SubmissionId == submissionId)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8604

        if (!await UseSubmissionRepositoryAsync(steps, submissionId, cancellationToken))
        {
            return null;
        }

        var index = steps.SingleOrDefault(s => s.Type == PackageValidation.IndexType);
        if (index == null || index.State != PackageValidationState.Pending)
        {
            return null;
        }

        activity?.SetTag(BaGetDiagnostics.Tags.PackageId, index.PackageId);
        activity?.SetTag(BaGetDiagnostics.Tags.PackageVersion, index.PackageVersion);

        using var package = new MemoryStream();
        using (var staged = await _storage.GetAsync(ValidatingPackageSubmissionService.StagedPath(_repository.Current, submissionId), cancellationToken))
        {
            await staged.CopyToAsync(package, cancellationToken);
        }

        package.Position = 0;

        using (var reader = new PackageArchiveReader(package, leaveStreamOpen: true))
        {
            var identity = await reader.GetIdentityAsync(cancellationToken);
            var context = new PackageValidationContext(identity.Id, identity.Version, package, reader);

            foreach (var validator in _validators)
            {
                var step = steps.FirstOrDefault(s => s.Type == validator.Name);
                if (step == null || step.State != PackageValidationState.Pending) continue;

                PackageValidatorResult result;
                try
                {
                    package.Position = 0;
                    result = await validator.ValidateAsync(context, cancellationToken);
                }
                catch (Exception e) when (!(e is OperationCanceledException) && !(e is MaintenanceException))
                {
                    _logger.LogError(e, "Validator {Validator} failed on submission {SubmissionId}", validator.Name, submissionId);
                    result = PackageValidatorResult.Invalid($"The {validator.Name} validator failed: {e.Message}");
                }

                Complete(step, result.IsValid, result.Message);
            }
        }

        // A step whose validator is no longer registered cannot pass.
        foreach (var orphan in steps.Where(s => s.State == PackageValidationState.Pending && s != index))
        {
            Complete(orphan, succeeded: false, $"The {orphan.Type} validator is no longer registered.");
        }

        if (steps.Any(s => s.State == PackageValidationState.Failed))
        {
            Complete(index, succeeded: false, "Quarantined: the package failed validation.");
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Submission {SubmissionId} failed validation and is quarantined", submissionId);
            return "Failed";
        }

        // Rejected (rows gone) while the validators ran, possibly on another node: do not publish what was refused.
        #pragma warning disable CS8604
        var stillPending = await _context.PackageValidations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(v => v.SubmissionId == submissionId && v.Type == PackageValidation.IndexType && v.State == PackageValidationState.Pending, cancellationToken);
        #pragma warning restore CS8604
        if (!stillPending)
        {
            _logger.LogWarning("Submission {SubmissionId} was rejected while it was validated; not indexed", submissionId);
            return null;
        }

        var indexError = await IndexAsync(package, _options.Value.RepositorySigningServiceIndexUrl, cancellationToken);
        Complete(index, indexError == null, indexError);

        if (indexError == null)
        {
            #pragma warning disable CS8604
            var version = NuGet.Versioning.NuGetVersion.Parse(index.PackageVersion);
            #pragma warning restore CS8604
            var indexed = await _packages.FindOrNullAsync(index.PackageId, version, includeUnlisted: true, cancellationToken);
            foreach (var step in steps)
            {
                step.PackageKey = indexed?.Key;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (indexError == null)
        {
            await _storage.DeleteAsync(ValidatingPackageSubmissionService.StagedPath(_repository.Current, submissionId), cancellationToken);
        }

        return indexError == null ? "Succeeded" : "Failed";
    }

    /// <summary>
    /// Switches the scope to the repository a submission belongs to.
    /// </summary>
    /// <returns>False when that repository no longer exists.</returns>
    internal async Task<bool> UseSubmissionRepositoryAsync(IReadOnlyList<PackageValidation> steps, string submissionId, CancellationToken cancellationToken)
    {
        if (steps.Count > 0 && steps[0].RepositoryKey != _repository.Current.Key)
        {
            var target = await _repositories.FindAsync(steps[0].RepositoryKey, cancellationToken);
            if (target == null)
            {
                _logger.LogWarning("Submission {SubmissionId} belongs to unknown repository {Key}", submissionId, steps[0].RepositoryKey);
                return false;
            }

            _repository.Use(target);
        }

        return true;
    }

    /// <summary>
    /// Indexes a quarantined submission although validation failed (an administrator's decision; the caller audits
    /// it). The validators' failed verdicts stay as they are; only the index step is completed.
    /// </summary>
    /// <returns>Null on success, otherwise why it was not released.</returns>
    /// <exception cref="MaintenanceException">The gate is closed.</exception>
    internal async Task<string?> ReleaseAsync(string submissionId, string? actor, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        var steps = await _context.PackageValidations
            .IgnoreQueryFilters()
            .Where(v => v.SubmissionId == submissionId)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8604

        if (!await UseSubmissionRepositoryAsync(steps, submissionId, cancellationToken)) return "The submission's repository no longer exists.";

        var index = steps.SingleOrDefault(s => s.Type == PackageValidation.IndexType);
        if (index == null) return "There is no such submission.";
        if (index.State != PackageValidationState.Failed) return "Only a quarantined submission can be released.";

        using var package = new MemoryStream();
        try
        {
            using var staged = await _storage.GetAsync(ValidatingPackageSubmissionService.StagedPath(_repository.Current, submissionId), cancellationToken);
            await staged.CopyToAsync(package, cancellationToken);
        }
        catch (Exception e) when (!(e is OperationCanceledException))
        {
            _logger.LogWarning(e, "The staged package of submission {SubmissionId} could not be read", submissionId);
            return "The staged package is missing.";
        }

        package.Position = 0;
        var indexError = await IndexAsync(package, _options.Value.RepositorySigningServiceIndexUrl, cancellationToken);
        if (indexError != null) return indexError;

        Complete(index, succeeded: true, $"Released by {actor ?? "an administrator"} although validation failed.");

        #pragma warning disable CS8604
        var version = NuGet.Versioning.NuGetVersion.Parse(index.PackageVersion);
        #pragma warning restore CS8604
        var indexed = await _packages.FindOrNullAsync(index.PackageId, version, includeUnlisted: true, cancellationToken);
        foreach (var step in steps)
        {
            step.PackageKey = indexed?.Key;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _storage.DeleteAsync(ValidatingPackageSubmissionService.StagedPath(_repository.Current, submissionId), cancellationToken);

        return null;
    }

    /// <returns>Null on success, otherwise why indexing failed.</returns>
    private async Task<string?> IndexAsync(Stream package, string? serviceIndexUrl, CancellationToken cancellationToken)
    {
        var toIndex = package;
        try
        {
            if (_signer.IsConfigured)
            {
                // NuGet only accepts a repository signature that names an https source.
                if (!Uri.TryCreate(serviceIndexUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                {
                    return "Repository signing needs Validation:RepositorySigningServiceIndexUrl to be an absolute https URL.";
                }

                toIndex = await _signer.SignAsync(package, serviceIndexUrl, cancellationToken);
            }

            toIndex.Position = 0;
            var result = await _indexer.IndexAsync(toIndex, cancellationToken);

            return result == PackageIndexingResult.Success ? null : $"Indexing failed: {result}.";
        }
        catch (Exception e) when (!(e is OperationCanceledException) && !(e is MaintenanceException))
        {
            // A closed maintenance gate is not the package's fault: let it reach the worker, which
            // retries later, instead of quarantining the submission.
            _logger.LogError(e, "Failed to index a validated package");
            return $"Indexing failed: {e.Message}";
        }
        finally
        {
            if (!ReferenceEquals(toIndex, package))
            {
                toIndex.Dispose();
            }
        }
    }

    private void Complete(PackageValidation step, bool succeeded, string? message)
    {
        step.State = succeeded ? PackageValidationState.Succeeded : PackageValidationState.Failed;
        step.Message = message?.Length > AbstractContext<DbContext>.DefaultMaxStringLength
            ? message.Substring(0, AbstractContext<DbContext>.DefaultMaxStringLength)
            : message;
        step.Completed = _time.UtcNow;
    }
}