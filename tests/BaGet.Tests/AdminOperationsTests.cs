using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Validation.Pipeline;

namespace BaGet.Tests;

/// <summary>
/// The administrator's Operations, Validation queue and Abuse reports pages (plan U5): background jobs per
/// repository with one slot each, quarantine retry/release/reject (audited, password confirmed), abuse reports of every
/// repository. Admin only; every write is refused while the maintenance gate is closed.
/// </summary>
public class AdminOperationsTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _admin;
    private readonly ToggleValidator _validator = new();
    private readonly BlockingOperation _blocking = new();
    private readonly RecordingAudit _audit = new();

    public AdminOperationsTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "Accounts:Enabled", "true" },
                { "ApiKey", "" },
                { "Validation:Enabled", "true" },
            },
            configureServices: services =>
            {
                services.AddSingleton<IPackageValidator>(_validator);
                services.AddSingleton<IAdminOperation>(_blocking);
                services.AddSingleton<IAdminOperation>(new FailingOperation());
                services.AddSingleton<IAdminAudit>(_audit);
                services.AddSingleton<IPackageDownloadsSource>(new EmptyDownloadsSource());
            });
        _admin = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _blocking.Release.TrySetResult(true);
        _admin.Dispose();
        _app.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> StartAsync(string operation, string repository = "default")
        => _admin.PostAsync("admin/operations", new Dictionary<string, string> { { "operation", operation }, { "repository", repository } });

    private async Task CreateRepositoryAsync(string name)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.NuGet, RepositoryKind.Local, Token);
    }

    private async Task<string> OperationsPageAsync() => await _admin.GetPageAsync("admin/operations");

    // ---- access ----

    [Fact]
    public async Task NonAdministratorsAndAnonymousDoNotSeeThePages()
    {
        await CreateUserAsync(_app, "alice");
        using var alice = new Browser(_app);
        await SignInAsync(alice, "alice");
        using var anonymous = new Browser(_app);

        foreach (var path in new[] { "admin/operations", "admin/validation", "admin/abuse" })
        {
            using var asUser = await alice.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, asUser.StatusCode);

            using var asAnonymous = await anonymous.GetAsync(path);
            Assert.NotEqual(HttpStatusCode.OK, asAnonymous.StatusCode);
        }

        using var post = await alice.RawPostAsync("admin/operations", new Dictionary<string, string> { { "operation", "catalog-rebuild" }, { "repository", "default" } });
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        Assert.Null(_app.Services.GetRequiredService<AdminJobService>().Get("catalog-rebuild", "default"));
    }

    [Fact]
    public async Task TheAdminMenuLinksToThePages()
    {
        var html = await _admin.GetPageAsync("admin/operations");

        Assert.Contains("href=\"/admin/operations\"", html);
        Assert.Contains("href=\"/admin/validation\"", html);
        Assert.Contains("href=\"/admin/abuse\"", html);
    }

    // ---- operations ----

    [Fact]
    public async Task ACatalogRebuildRunsInTheBackgroundAndReportsItsCounts()
    {
        var jobs = _app.Services.GetRequiredService<AdminJobService>();
        using var started = await StartAsync("catalog-rebuild");
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        await jobs.WaitAsync("catalog-rebuild", "default");

        var status = jobs.Get("catalog-rebuild", "default");
        Assert.Equal(AdminJobState.Succeeded, status!.State);
        Assert.Equal("root", status.Actor);
        Assert.Contains("details entries", status.Message);

        var html = await OperationsPageAsync();
        Assert.Contains("Rebuild the catalog started on default.", html);
        Assert.Contains("Succeeded", html);
        Assert.Contains(_audit.Entries, e => e.Action == "operation.catalog-rebuild.start" && e.Actor == "root");
        Assert.Contains(_audit.Entries, e => e.Action == "operation.catalog-rebuild.succeeded");
    }

    [Fact]
    public async Task ADownloadsImportRunsOnANonDefaultRepository()
    {
        await CreateRepositoryAsync("second");
        var jobs = _app.Services.GetRequiredService<AdminJobService>();

        (await StartAsync("downloads-import", "second")).Dispose();
        await jobs.WaitAsync("downloads-import", "second");

        Assert.Equal(AdminJobState.Succeeded, jobs.Get("downloads-import", "second")!.State);
        Assert.Null(jobs.Get("downloads-import", "default"));
    }

    [Fact]
    public async Task OneRunAtATimePerOperationAndRepository()
    {
        await CreateRepositoryAsync("second");
        var jobs = _app.Services.GetRequiredService<AdminJobService>();

        (await StartAsync(BlockingOperation.OperationName)).Dispose();
        await _blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        Assert.Equal(AdminJobState.Running, jobs.Get(BlockingOperation.OperationName, "default")!.State);
        (await StartAsync(BlockingOperation.OperationName)).Dispose();
        Assert.Contains("already running on default", await OperationsPageAsync());
        Assert.Equal(1, _blocking.Starts);

        // Another repository has its own slot.
        (await StartAsync(BlockingOperation.OperationName, "second")).Dispose();
        SpinWait.SpinUntil(() => _blocking.Starts == 2, TimeSpan.FromSeconds(30));
        Assert.Equal(2, _blocking.Starts);

        _blocking.Release.SetResult(true);
        await jobs.WaitAsync(BlockingOperation.OperationName, "default");
        await jobs.WaitAsync(BlockingOperation.OperationName, "second");
        Assert.Equal(AdminJobState.Succeeded, jobs.Get(BlockingOperation.OperationName, "default")!.State);

        // Free again.
        (await StartAsync(BlockingOperation.OperationName)).Dispose();
        await jobs.WaitAsync(BlockingOperation.OperationName, "default");
        Assert.Equal(3, _blocking.Starts);
    }

    [Fact]
    public async Task AFailedJobShowsItsError()
    {
        var jobs = _app.Services.GetRequiredService<AdminJobService>();

        (await StartAsync(FailingOperation.OperationName)).Dispose();
        await jobs.WaitAsync(FailingOperation.OperationName, "default");

        Assert.Equal(AdminJobState.Failed, jobs.Get(FailingOperation.OperationName, "default")!.State);
        var html = await OperationsPageAsync();
        Assert.Contains("InvalidOperationException", html);
        Assert.Contains("boom", html);
    }

    [Fact]
    public async Task UnknownOperationsAndRepositoriesAre404()
    {
        using var unknown = await StartAsync("nope");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var noRepository = await StartAsync("catalog-rebuild", "missing");
        Assert.Equal(HttpStatusCode.NotFound, noRepository.StatusCode);
    }

    [Fact]
    public async Task StartingIsRefusedWhileTheGateIsClosed()
    {
        _app.Services.GetRequiredService<MaintenanceGate>().Close("test");

        using var response = await StartAsync("catalog-rebuild");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(_app.Services.GetRequiredService<AdminJobService>().Get("catalog-rebuild", "default"));
    }

    // ---- validation ----

    private async Task<string> QuarantineAsync(string id, string repository = "default")
    {
        _validator.Fail = true;
        string key;
        using (var scope = _app.Services.CreateScope())
        {
            var user = await FindAsync(_app, "root");
            key = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
                .CreateAsync(user!.Id, "test", new[] { ApiKeyScopes.Push }, "*", null, Token);
        }

        using var client = _app.CreateClient();
        using var package = TestPackages.Build(id, "1.0.0");
        using var request = TestPackages.PushRequest(package, key);
        if (repository != "default") request.RequestUri = new Uri($"r/{repository}/api/v2/package", UriKind.Relative);
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return (await WaitForSubmissionAsync(id, ValidationSubmissionState.Quarantined)).SubmissionId;
    }

    private async Task<ValidationSubmissionSummary> WaitForSubmissionAsync(string id, ValidationSubmissionState state)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var found = (await ListAsync()).FirstOrDefault(s => s.PackageId == id && s.State == state);
            if (found != null) return found;
            Assert.True(DateTime.UtcNow < deadline, $"{id} did not reach {state}");
            await Task.Delay(50, Token);
        }
    }

    private async Task<IReadOnlyList<ValidationSubmissionSummary>> ListAsync()
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IValidationAdministration>().ListAsync(Token);
    }

    private Task<HttpResponseMessage> ActAsync(string handler, string submissionId, string password = Password)
        => _admin.PostAsync("admin/validation?handler=" + handler, new Dictionary<string, string> { { "submissionId", submissionId }, { "password", password } }, tokenPage: "admin/operations");

    private async Task<HttpStatusCode> DownloadStatusAsync(string id, string repository = "default")
    {
        using var client = _app.CreateClient();
        var prefix = repository == "default" ? string.Empty : $"r/{repository}/";
        using var response = await client.GetAsync($"{prefix}v3/package/{id.ToLowerInvariant()}/1.0.0/{id.ToLowerInvariant()}.1.0.0.nupkg", Token);

        return response.StatusCode;
    }

    [Fact]
    public async Task AQuarantinedPackageIsListedWithTheReason()
    {
        await QuarantineAsync("Listed.Package");

        var html = await _admin.GetPageAsync("admin/validation");

        Assert.Contains("Listed.Package", html);
        Assert.Contains("Quarantined", html);
        Assert.Contains("Toggle: Rejected by the test.", html);
        Assert.Contains("data-repository=\"default\"", html);
    }

    [Fact]
    public async Task RetryValidatesAgainAndIndexesWhenItNowPasses()
    {
        var submission = await QuarantineAsync("Retried.Package");
        Assert.Equal(HttpStatusCode.NotFound, await DownloadStatusAsync("Retried.Package"));

        _validator.Fail = false;
        using var response = await ActAsync("Retry", submission, password: string.Empty);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await DownloadStatusAsync("Retried.Package") != HttpStatusCode.OK)
        {
            Assert.True(DateTime.UtcNow < deadline, "the retried package was never indexed");
            await Task.Delay(50, Token);
        }

        // The worker indexes the package before it removes the submission, so the package can be served a moment before the
        // queue forgets the submission: wait for that too (a submission that is never removed still fails at the deadline).
        while ((await ListAsync()).Any(s => s.SubmissionId == submission))
        {
            Assert.True(DateTime.UtcNow < deadline, "the retried submission was never removed from the queue");
            await Task.Delay(50, Token);
        }
    }

    [Fact]
    public async Task ReleaseIndexesTheQuarantinedPackageAndIsAudited()
    {
        var submission = await QuarantineAsync("Released.Package");

        using var response = await ActAsync("Release", submission);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.Equal(HttpStatusCode.OK, await DownloadStatusAsync("Released.Package"));
        Assert.Empty(await ListAsync());
        var audit = Assert.Single(_audit.Entries, e => e.Action == "validation.release");
        Assert.Equal("root", audit.Actor);
        Assert.Contains("Released.Package", audit.Subject);
        Assert.Contains("Rejected by the test.", audit.Details);
        Assert.Contains("Released by root", await ReleaseMessageAsync(submission));
    }

    private async Task<string> ReleaseMessageAsync(string submission)
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(context.PackageValidations!)
                    .Where(v => v.SubmissionId == submission && v.Type == PackageValidation.IndexType), Token))
            .Single().Message ?? string.Empty;
    }

    [Fact]
    public async Task ReleaseAndRejectNeedTheCorrectPassword()
    {
        var submission = await QuarantineAsync("Guarded.Package");

        foreach (var handler in new[] { "Release", "Reject" })
        {
            using var response = await ActAsync(handler, submission, password: "wrong");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("confirmation is incorrect", await _admin.GetPageAsync("admin/validation"));
        }

        Assert.Equal(HttpStatusCode.NotFound, await DownloadStatusAsync("Guarded.Package"));
        Assert.Single(await ListAsync());
        Assert.DoesNotContain(_audit.Entries, e => e.Action.StartsWith("validation.re", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectDeletesTheStagedFileAndTheRows()
    {
        var submission = await QuarantineAsync("Rejected.Package");
        var storage = _app.Services.GetRequiredService<IStorageService>();
        var path = $"validation/{submission}.nupkg";
        (await storage.GetAsync(path, Token)).Dispose();

        using var response = await ActAsync("Reject", submission);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.Empty(await ListAsync());
        await Assert.ThrowsAnyAsync<Exception>(async () => (await storage.GetAsync(path, Token)).Dispose());
        Assert.Equal(HttpStatusCode.NotFound, await DownloadStatusAsync("Rejected.Package"));
        Assert.Contains(_audit.Entries, e => e.Action == "validation.reject");
    }

    [Fact]
    public async Task SubmissionsOfOtherRepositoriesAreListedAndReleasedThere()
    {
        await CreateRepositoryAsync("second");
        var submission = await QuarantineAsync("Second.Package", "second");

        Assert.Contains("data-repository=\"second\"", await _admin.GetPageAsync("admin/validation"));
        (await ActAsync("Release", submission)).Dispose();

        Assert.Equal(HttpStatusCode.OK, await DownloadStatusAsync("Second.Package", "second"));
        Assert.Equal(HttpStatusCode.NotFound, await DownloadStatusAsync("Second.Package"));
    }

    [Fact]
    public async Task ValidationWritesAreRefusedWhileTheGateIsClosed()
    {
        var submission = await QuarantineAsync("Frozen.Package");
        _app.Services.GetRequiredService<MaintenanceGate>().Close("test");

        foreach (var handler in new[] { "Retry", "Release", "Reject" })
        {
            using var response = await ActAsync(handler, submission);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, await DownloadStatusAsync("Frozen.Package"));
    }

    [Fact]
    public async Task AnUnknownSubmissionIsRefusedWithAMessage()
    {
        (await ActAsync("Release", "does-not-exist")).Dispose();

        var page = await _admin.GetPageAsync("admin/validation");
        Assert.True(page.Contains("There is no such submission."), page);
    }

    // ---- abuse ----

    private async Task<int> AddReportAsync(string repository, string id, bool resolved = false)
    {
        using var scope = _app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var info = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository, Token);
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use(info!);
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var report = new AbuseReport
        {
            PackageId = id,
            PackageVersion = "1.0.0",
            Reason = AbuseReport.Reasons[0],
            Details = "details of " + id,
            Created = DateTime.UtcNow,
            Resolved = resolved ? DateTime.UtcNow : null,
        };
        context.AbuseReports!.Add(report);
        await context.SaveChangesAsync(Token);

        return report.Key;
    }

    [Fact]
    public async Task AbuseReportsOfEveryRepositoryAreListedAndResolvedOnesHiddenByDefault()
    {
        await CreateRepositoryAsync("second");
        await AddReportAsync("default", "Open.Default");
        await AddReportAsync("second", "Open.Second");
        await AddReportAsync("default", "Done.Default", resolved: true);

        var html = await _admin.GetPageAsync("admin/abuse");
        Assert.Contains("Open.Default", html);
        Assert.Contains("Open.Second", html);
        Assert.Contains("data-repository=\"second\"", html);
        Assert.DoesNotContain("Done.Default", html);
        Assert.True(html.Contains("href=\"/packages/Open.Default/manage\""), html);
        Assert.True(html.Contains("href=\"/r/second/packages/Open.Second/manage\""), html);

        Assert.Contains("Done.Default", await _admin.GetPageAsync("admin/abuse?all=true"));
    }

    [Fact]
    public async Task ResolvingAndReopeningAReportWorks()
    {
        await CreateRepositoryAsync("second");
        var key = await AddReportAsync("second", "Toggled.Package");

        (await _admin.PostAsync("admin/abuse?handler=Resolve", new Dictionary<string, string> { { "key", key.ToString() } })).Dispose();
        var html = await _admin.GetPageAsync("admin/abuse");
        Assert.DoesNotContain("Toggled.Package", html.Substring(html.IndexOf("abuse-table", StringComparison.Ordinal) < 0 ? 0 : html.IndexOf("abuse-table", StringComparison.Ordinal)));
        Assert.Contains(_audit.Entries, e => e.Action == "abuse.resolve" && e.Subject == $"report {key}");

        (await _admin.PostAsync("admin/abuse?all=true&handler=Reopen", new Dictionary<string, string> { { "key", key.ToString() } })).Dispose();
        Assert.Contains("Toggled.Package", await _admin.GetPageAsync("admin/abuse"));
    }

    [Fact]
    public async Task ResolvingIsRefusedWhileTheGateIsClosedAndAMissingReportIs404()
    {
        var key = await AddReportAsync("default", "Frozen.Report");

        using var missing = await _admin.PostAsync("admin/abuse?handler=Resolve", new Dictionary<string, string> { { "key", "9999" } });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        _app.Services.GetRequiredService<MaintenanceGate>().Close("test");
        using var response = await _admin.PostAsync("admin/abuse?handler=Resolve", new Dictionary<string, string> { { "key", key.ToString() } });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ---- fakes ----

    private sealed class ToggleValidator : IPackageValidator
    {
        public volatile bool Fail;

        public string Name => "Toggle";

        public Task<PackageValidatorResult> ValidateAsync(PackageValidationContext context, CancellationToken cancellationToken)
            => Task.FromResult(Fail ? PackageValidatorResult.Invalid("Rejected by the test.") : PackageValidatorResult.Valid);
    }

    private sealed class BlockingOperation : IAdminOperation
    {
        public const string OperationName = "test-blocking";
        private int _starts;

        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Starts => Volatile.Read(ref _starts);

        public string Name => OperationName;

        public string Title => "Blocking";

        public string Description => "Waits for the test.";

        public bool IsAvailable(IServiceProvider services) => true;

        public async Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _starts);
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);

            return "released";
        }
    }

    private sealed class FailingOperation : IAdminOperation
    {
        public const string OperationName = "test-failing";

        public string Name => OperationName;

        public string Title => "Failing";

        public string Description => "Always fails.";

        public bool IsAvailable(IServiceProvider services) => true;

        public Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    // The real source downloads nuget.org's counts.
    private sealed class EmptyDownloadsSource : IPackageDownloadsSource
    {
        public Task<Dictionary<string, Dictionary<string, long>>> GetPackageDownloadsAsync()
            => Task.FromResult(new Dictionary<string, Dictionary<string, long>>());
    }

    private sealed class RecordingAudit : IAdminAudit
    {
        public ConcurrentQueue<(string? Actor, string Action, string Subject, string? Details)> Entries { get; } = new();

        public void Record(string? actor, string action, string subject, string? details = null)
            => Entries.Enqueue((actor, action, subject, details));
    }
}
