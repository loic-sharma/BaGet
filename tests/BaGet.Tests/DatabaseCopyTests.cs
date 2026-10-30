using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Tests.Support;
using BaGet.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// Copying the database from the Settings page, SQLite to SQLite, through the real page and worker:
/// every table is equal afterwards, a failure leaves the source intact and the target empty, and the
/// settings are saved only after a verified copy.
/// </summary>
public class DatabaseCopyTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "baget-copy-" + Guid.NewGuid().ToString("N"));

    public DatabaseCopyTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables)) disposable.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    /// <summary>An existing, empty SQLite database (a zero-length file is one) to copy into.</summary>
    private string NewTarget()
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".db");
        File.WriteAllBytes(path, Array.Empty<byte>());
        return $"Data Source={path}";
    }

    private async Task<(BaGetApplication App, Browser Admin)> StartAsync(Action<IServiceCollection>? configureServices = null)
    {
        var app = new BaGetApplication(
            _output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" } },
            configureServices: configureServices);
        _disposables.Add(app);

        await AdminTestSupport.CreateUserAsync(app, "root", administrator: true);
        await AdminTestSupport.CreateUserAsync(app, "alice");
        await AdminTestSupport.PushAsync(app, "root", "Copy.One");
        await AdminTestSupport.PushAsync(app, "root", "Copy.One", "2.0.0");
        await AdminTestSupport.PushAsync(app, "alice", "Copy.Two");
        await AdminTestSupport.AddOrganizationAsync(app, "Acme", ("alice", true));
        await AdminTestSupport.AddOwnerAsync(app, "Copy.One", "Acme");

        var admin = new Browser(app);
        _disposables.Add(admin);
        await AdminTestSupport.SignInAsync(admin, "root");
        return (App: app, Admin: admin);
    }

    private static Task<HttpResponseMessage> PostDatabaseAsync(
        Browser admin, string type, string connectionString, bool copyData = true, string? password = null)
    {
        var fields = new Dictionary<string, string>
        {
            { "section", "Database" },
            { "password", password ?? AdminTestSupport.Password },
            { "v:Database:Type", type },
            { "v:Database:ConnectionString", connectionString },
        };
        if (copyData) fields["copyData"] = "true";

        return admin.PostAsync("admin/settings", fields);
    }

    private static async Task<DatabaseCopyStatus> WaitAsync(BaGetApplication app)
    {
        var copy = app.Services.GetRequiredService<DatabaseCopyService>();
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!copy.Status.IsFinished)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The copy did not finish: " + copy.Status.Phase);
            await Task.Delay(50);
        }

        return copy.Status;
    }

    private static IReadOnlyDictionary<string, string> SavedSettings(BaGetApplication app)
        => app.Services.GetRequiredService<SettingsOverridesStore>().Read();

    private static DbContext OpenTarget(string connectionString)
        => (DbContext)new SqliteDatabaseContextFactory().Create(connectionString);

    private static DbContext OpenSource(BaGetApplication app, out IServiceScope scope)
    {
        scope = app.Services.CreateScope();
        return (DbContext)scope.ServiceProvider.GetRequiredService<IContext>();
    }

    private static async Task<Dictionary<string, long>> CountsAsync(DbContext context)
    {
        var counts = new Dictionary<string, long>();
        foreach (var table in DatabaseTable.InDependencyOrder(context.Model))
        {
            counts[table.Name] = await table.CountAsync(context, CancellationToken.None);
        }

        return counts;
    }

    private static async Task AssertTablesEqualAsync(DbContext expected, DbContext actual)
    {
        foreach (var table in DatabaseTable.InDependencyOrder(actual.Model))
        {
            var left = await table.ReadBatchAsync(expected, 0, int.MaxValue, CancellationToken.None);
            var right = await table.ReadBatchAsync(actual, 0, int.MaxValue, CancellationToken.None);
            Assert.Equal(left.Count, right.Count);

            var compared = table.EntityType.GetProperties()
                .Where(p => p.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate)
                .ToList();
            for (var i = 0; i < left.Count; i++)
            {
                foreach (var property in compared)
                {
                    Assert.Equal(
                        DatabaseTable.Format(DatabaseTable.ValueOf(property, left[i].Entity)),
                        DatabaseTable.Format(DatabaseTable.ValueOf(property, right[i].Entity)));
                }

                // Shadow properties (the foreign key of a package's types, frameworks and dependencies) too.
                Assert.Equal(
                    left[i].Shadow.OrderBy(p => p.Key).Select(p => p.Key + "=" + DatabaseTable.Format(p.Value)),
                    right[i].Shadow.OrderBy(p => p.Key).Select(p => p.Key + "=" + DatabaseTable.Format(p.Value)));
            }
        }
    }

    private static async Task<string> CreateKeyAsync(BaGetApplication app, string userName)
    {
        var user = await AdminTestSupport.FindAsync(app, userName);
        using var scope = app.Services.CreateScope();
        #pragma warning disable CS8602
        return await scope.ServiceProvider.GetRequiredService<ApiKeyService>().CreateAsync(
            user.Id, "test", new[] { ApiKeyScopes.Push }, "*", null, CancellationToken.None);
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task CopiesEveryTableSavesTheSettingsAndStaysReadOnlyUntilRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync();
        var target = NewTarget();
        var gate = app.Services.GetRequiredService<MaintenanceGate>();
        var key = await CreateKeyAsync(app, "root");

        using (var response = await PostDatabaseAsync(admin, "Sqlite", target))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var status = await WaitAsync(app);
        Assert.True(status.Phase == DatabaseCopyPhase.Succeeded, string.Join(" ", status.Errors));
        Assert.True(status.RestartRequired);
        Assert.Equal(status.RowsTotal, status.RowsCopied);

        using (var targetDb = OpenTarget(target))
        {
            var sourceDb = OpenSource(app, out var scope);
            using (scope)
            {
                await AssertTablesEqualAsync(sourceDb, targetDb);

                var counts = await CountsAsync(targetDb);
                Assert.Equal(await CountsAsync(sourceDb), counts);
                Assert.True(counts["Packages"] >= 3);
                Assert.True(counts["Users"] >= 2);
                Assert.True(counts["Organizations"] >= 1);

                // A key generated after the copy continues after the highest copied one.
                var highest = await targetDb.Set<Organization>().MaxAsync(o => o.Key, cancellationToken);
                var later = new Organization { Name = "Later", Created = DateTime.UtcNow };
                targetDb.Set<Organization>().Add(later);
                await targetDb.SaveChangesAsync(cancellationToken);
                Assert.Equal(highest + 1, later.Key);
            }
        }

        Assert.Equal(target, SavedSettings(app)["Database:ConnectionString"]);

        // Closed until the restart: a write on the old database would be lost.
        Assert.True(gate.IsClosedUntilRestart);
        Assert.False(gate.Open());
        using var client = app.CreateClient();
        using var package = TestPackages.Build("After.Copy", "1.0.0");
        using var push = TestPackages.PushRequest(package, key);
        using var refused = await client.SendAsync(push, cancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);

        var html = await admin.GetPageAsync("admin/settings");
        Assert.Contains("Restart BaGet now", html);
    }

    [Fact]
    public async Task EveryRepositoryIsCopiedWithItsRows()
    {
        var (app, admin) = await StartAsync();
        var target = NewTarget();

        using (var scope = app.Services.CreateScope())
        {
            var second = await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .CreateAsync("second", RepositoryFormat.NuGet, RepositoryKind.Local, CancellationToken.None);
            using var inSecond = app.Services.GetRequiredService<IServiceScopeFactory>().CreateRepositoryScope(second);
            using var package = TestPackages.Build("Copy.Three", "1.0.0");
            Assert.Equal(
                PackageIndexingResult.Success,
                await inSecond.ServiceProvider.GetRequiredService<IPackageIndexingService>().IndexAsync(package, CancellationToken.None));
        }

        using (var response = await PostDatabaseAsync(admin, "Sqlite", target))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var status = await WaitAsync(app);
        Assert.True(status.Phase == DatabaseCopyPhase.Succeeded, string.Join(" ", status.Errors));

        using var targetDb = OpenTarget(target);
        var sourceDb = OpenSource(app, out var sourceScope);
        using (sourceScope)
        {
            await AssertTablesEqualAsync(sourceDb, targetDb);

            Assert.Equal(new[] { "default", "second" }, targetDb.Set<Repository>().OrderBy(r => r.Key).Select(r => r.Name).ToList());
            Assert.Equal(1, await targetDb.Set<Package>().IgnoreQueryFilters().CountAsync(p => p.RepositoryKey == 2, TestContext.Current.CancellationToken));
            Assert.Equal(
                await sourceDb.Set<Package>().IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken),
                await targetDb.Set<Package>().IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task AnAdministratorCanSignInAfterTheCopyWhileTheGateIsClosedAndReachTheStatus()
    {
        var (app, admin) = await StartAsync();
        var target = NewTarget();

        // A failed attempt earlier: a successful sign-in would normally reset the counter, which is a write.
        using (var scopeForCounter = app.Services.CreateScope())
        {
            var users = scopeForCounter.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<BaGetUser>>();
            var root = (await users.FindByNameAsync("root"))!;
            await users.AccessFailedAsync(root);
            Assert.Equal(1, await users.GetAccessFailedCountAsync(root));
        }

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        Assert.Equal(DatabaseCopyPhase.Succeeded, (await WaitAsync(app)).Phase);
        Assert.True(app.Services.GetRequiredService<MaintenanceGate>().IsClosedUntilRestart);

        using var later = new Browser(app);
        await AdminTestSupport.SignInAsync(later, "root");

        var json = await later.GetPageAsync("admin/settings?handler=CopyStatus");
        Assert.Contains("\"Succeeded\"", json);
        Assert.Contains("Restart BaGet now", await later.GetPageAsync("admin/settings"));

        // Everything else stays refused: a wrong password is a failed attempt, which is counted (a write).
        using var wrong = new Browser(app);
        using var refused = await wrong.PostAsync("account/login", new Dictionary<string, string>
        {
            { "UserName", "root" },
            { "Password", "not-the-password" },
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
    }

    [Fact]
    public async Task ANonEmptyTargetIsRefusedAndNotTouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync();
        var target = NewTarget();
        using (var targetDb = OpenTarget(target))
        {
            await ((IContext)targetDb).RunMigrationsAsync(CancellationToken.None);
            targetDb.Set<Organization>().Add(new Organization { Name = "Existing", Created = DateTime.UtcNow });
            await targetDb.SaveChangesAsync(cancellationToken);
        }

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        var status = await WaitAsync(app);

        Assert.Equal(DatabaseCopyPhase.Failed, status.Phase);
        Assert.Contains(status.Errors, e => e.Contains("not empty") && e.Contains("Organizations"));
        Assert.DoesNotContain("Database:ConnectionString", SavedSettings(app).Keys);
        Assert.False(app.Services.GetRequiredService<MaintenanceGate>().IsClosed);

        using var after = OpenTarget(target);
        Assert.Equal(1, await after.Set<Organization>().CountAsync(cancellationToken));
        Assert.Equal("Existing", (await after.Set<Organization>().SingleAsync(cancellationToken)).Name);
        Assert.Equal(0, await after.Set<Package>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task AnUnreachableTargetReportsAnErrorWithoutTheConnectionStringAndChangesNothing()
    {
        var (app, admin) = await StartAsync();
        var target = $"Data Source={Path.Combine(_directory, "missing", "folder", "x.db")};Password=s3cret-value";

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        var status = await WaitAsync(app);

        Assert.Equal(DatabaseCopyPhase.Failed, status.Phase);
        Assert.Contains(status.Errors, e => e.Contains("Cannot connect"));
        foreach (var error in status.Errors)
        {
            Assert.DoesNotContain("missing", error);
            Assert.DoesNotContain("s3cret-value", error);
        }

        Assert.DoesNotContain("Database:ConnectionString", SavedSettings(app).Keys);
        Assert.False(app.Services.GetRequiredService<MaintenanceGate>().IsClosed);

        var json = await admin.GetPageAsync("admin/settings?handler=CopyStatus");
        Assert.DoesNotContain("s3cret-value", json);
    }

    [Fact]
    public async Task ACopyEmitsADatabaseCopySpanWithOneChildSpanPerTableAndNoConnectionString()
    {
        using var capture = new DiagnosticsCapture();
        var (app, admin) = await StartAsync();
        var target = NewTarget();

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        Assert.Equal(DatabaseCopyPhase.Succeeded, (await WaitAsync(app)).Phase);

        var root = Assert.Single(capture.Spans("database.copy"));
        Assert.Equal("Succeeded", DiagnosticsCapture.Tag(root, BaGetDiagnostics.Tags.Result));
        Assert.Equal("Sqlite", DiagnosticsCapture.Tag(root, BaGetDiagnostics.Tags.TargetType));
        Assert.Equal("Sqlite", DiagnosticsCapture.Tag(root, BaGetDiagnostics.Tags.SourceType));

        var tables = capture.Spans("database.copy.table");
        Assert.NotEmpty(tables);
        Assert.Equal(DiagnosticsCapture.Tag(root, BaGetDiagnostics.Tags.Tables), tables.Count);
        Assert.All(tables, t =>
        {
            Assert.Equal(root.Id, t.ParentId);
            Assert.Equal(root.TraceId, t.TraceId);
            Assert.Equal("Succeeded", DiagnosticsCapture.Tag(t, BaGetDiagnostics.Tags.Result));
        });
        Assert.Contains(tables, t => (string?)DiagnosticsCapture.Tag(t, BaGetDiagnostics.Tags.Table) == "Packages"
                                     && (long)DiagnosticsCapture.Tag(t, BaGetDiagnostics.Tags.Rows)! >= 3);

        var everyTag = capture.Stopped.SelectMany(a => a.TagObjects).Select(t => t.Value?.ToString() ?? string.Empty);
        Assert.DoesNotContain(everyTag, v => v.Contains(target) || v.Contains("Data Source"));
    }

    [Fact]
    public async Task AFailedCopyMarksTheSpansAsErrors()
    {
        using var capture = new DiagnosticsCapture();
        var (app, admin) = await StartAsync();
        var copy = app.Services.GetRequiredService<DatabaseCopyService>();
        copy.AfterBatch = (table, rows, token) =>
            table == "Packages" ? throw new InvalidOperationException("injected failure") : Task.CompletedTask;

        using (await PostDatabaseAsync(admin, "Sqlite", NewTarget())) { }
        Assert.Equal(DatabaseCopyPhase.Failed, (await WaitAsync(app)).Phase);

        var root = Assert.Single(capture.Spans("database.copy"));
        Assert.Equal(System.Diagnostics.ActivityStatusCode.Error, root.Status);
        Assert.Equal("Failed", DiagnosticsCapture.Tag(root, BaGetDiagnostics.Tags.Result));
        var failed = Assert.Single(capture.Spans("database.copy.table"), t => (string?)DiagnosticsCapture.Tag(t, BaGetDiagnostics.Tags.Result) == "Failed");
        Assert.Equal("Packages", DiagnosticsCapture.Tag(failed, BaGetDiagnostics.Tags.Table));
        Assert.DoesNotContain("injected failure", failed.StatusDescription ?? string.Empty);
    }

    [Fact]
    public async Task AFailureMidCopyLeavesTheSourceIntactTheTargetEmptyAndTheSettingsUnchanged()
    {
        var (app, admin) = await StartAsync();
        var target = NewTarget();
        var copy = app.Services.GetRequiredService<DatabaseCopyService>();
        var gate = app.Services.GetRequiredService<MaintenanceGate>();

        var sourceDb = OpenSource(app, out var scope);
        Dictionary<string, long> before;
        using (scope) before = await CountsAsync(sourceDb);

        var batches = 0;
        copy.AfterBatch = (table, rows, token) =>
        {
            // Fail after a few tables are already in the target.
            if (table == "Packages" && ++batches >= 1) throw new InvalidOperationException("injected failure");
            return Task.CompletedTask;
        };

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        var status = await WaitAsync(app);

        Assert.Equal(DatabaseCopyPhase.Failed, status.Phase);
        Assert.Contains(status.Errors, e => e.Contains("injected failure"));
        Assert.False(gate.IsClosed);
        Assert.DoesNotContain("Database:ConnectionString", SavedSettings(app).Keys);

        using (var targetDb = OpenTarget(target))
        {
            Assert.All(await CountsAsync(targetDb), pair => Assert.Equal(0, pair.Value));
        }

        var sourceAfter = OpenSource(app, out var scopeAfter);
        using (scopeAfter) Assert.Equal(before, await CountsAsync(sourceAfter));

        // The server is writable again and the copy can be retried.
        copy.AfterBatch = null;
        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        Assert.Equal(DatabaseCopyPhase.Succeeded, (await WaitAsync(app)).Phase);
    }

    [Fact]
    public async Task AFailureAfterTheSettingsAreSavedKeepsTheCopiedTargetAndStaysReadOnly()
    {
        var (app, admin) = await StartAsync();
        var target = NewTarget();
        var copy = app.Services.GetRequiredService<DatabaseCopyService>();
        var gate = app.Services.GetRequiredService<MaintenanceGate>();
        copy.AfterSettingsSaved = () => throw new InvalidOperationException("injected after save");

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        var status = await WaitAsync(app);

        Assert.Equal(DatabaseCopyPhase.Failed, status.Phase);
        Assert.Contains(status.Errors, e => e.Contains("injected after save"));
        Assert.DoesNotContain("no setting was saved", status.Message);
        Assert.True(gate.IsClosedUntilRestart);
        Assert.Equal(target, SavedSettings(app)["Database:ConnectionString"]);

        using var targetDb = OpenTarget(target);
        var sourceDb = OpenSource(app, out var scope);
        using (scope) Assert.Equal(await CountsAsync(sourceDb), await CountsAsync(targetDb));
    }

    [Fact]
    public async Task ACancellationWhileTheSettingsAreBeingSavedBeforeTheFileIsReplacedRollsTheCopyBack()
    {
        var (app, _) = await StartAsync();
        var target = NewTarget();
        var copy = app.Services.GetRequiredService<DatabaseCopyService>();
        var gate = app.Services.GetRequiredService<MaintenanceGate>();
        var catalog = app.Services.GetRequiredService<SettingsCatalog>();
        var request = new DatabaseCopyRequest(
            "Sqlite",
            target,
            null,
            new Dictionary<SettingDescriptor, object>
            {
                { catalog.Find("Database:Type")!, "Sqlite" },
                { catalog.Find("Database:ConnectionString")!, target },
            },
            "root");

        using var cancellation = new CancellationTokenSource();
        copy.BeforeSettingsSaved = () =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        // What the queue does: the gate is closed, then the copy runs.
        gate.Close(DatabaseCopyService.GateReason);
        await copy.RunAsync(request, cancellation.Token);

        var status = copy.Status;
        Assert.Equal(DatabaseCopyPhase.Failed, status.Phase);
        Assert.False(gate.IsClosed);
        Assert.DoesNotContain("Database:ConnectionString", SavedSettings(app).Keys);
        using var targetDb = OpenTarget(target);
        Assert.All(await CountsAsync(targetDb), pair => Assert.Equal(0, pair.Value));
    }

    [Fact]
    public async Task APushOrSettingsSaveDuringTheCopyIsRefusedWith503AndASecondCopyIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync();
        var target = NewTarget();
        var copy = app.Services.GetRequiredService<DatabaseCopyService>();
        var key = await CreateKeyAsync(app, "root");

        var reached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        copy.AfterBatch = async (table, rows, token) =>
        {
            reached.TrySetResult(true);
            await release.Task;
        };

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);

        using var client = app.CreateClient();
        using (var package = TestPackages.Build("During.Copy", "1.0.0"))
        using (var push = TestPackages.PushRequest(package, key))
        using (var refused = await client.SendAsync(push, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.True(refused.Headers.RetryAfter != null);
        }

        using (var second = await PostDatabaseAsync(admin, "Sqlite", NewTarget()))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        }

        using (var save = await admin.PostAsync("admin/settings", new Dictionary<string, string>
               {
                   { "section", "Database" },
                   { "password", AdminTestSupport.Password },
                   { "v:Database:MySqlServerVersion", "8.0.0-mysql" },
               }))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, save.StatusCode);
        }

        Assert.True(copy.Status.IsActive);
        Assert.Equal(DatabaseCopyPhase.Copying, copy.Status.Phase);

        release.SetResult(true);
        var status = await WaitAsync(app);
        Assert.True(status.Phase == DatabaseCopyPhase.Succeeded, string.Join(" ", status.Errors));

        // Nothing the refused push wrote: the target has only the packages that existed.
        using var targetDb = OpenTarget(target);
        Assert.False(await targetDb.Set<Package>().AnyAsync(p => p.Id == "During.Copy", cancellationToken));
    }

    [Fact]
    public async Task ATargetThatDoesNotExistYetIsCreatedAndFilled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync();
        var target = $"Data Source={Path.Combine(_directory, "brand-new.db")}";

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        var status = await WaitAsync(app);

        Assert.True(status.Phase == DatabaseCopyPhase.Succeeded, string.Join(" ", status.Errors));
        using var targetDb = OpenTarget(target);
        Assert.Equal(3, await targetDb.Set<Package>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task StatusEndpointIsForAdministratorsOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync();

        using (var anonymous = new Browser(app))
        using (var response = await anonymous.GetAsync("admin/settings?handler=CopyStatus"))
        {
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        using (var alice = new Browser(app))
        {
            await AdminTestSupport.SignInAsync(alice, "alice");
            using var response = await alice.GetAsync("admin/settings?handler=CopyStatus");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var ok = await admin.GetAsync("admin/settings?handler=CopyStatus");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var json = JsonDocument.Parse(await ok.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Idle", json.RootElement.GetProperty("phase").GetString());
        Assert.False(json.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task ASqliteTargetAcceptsOverLongValuesAndCaseOnlyDuplicatesWhichItStores()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync();
        var target = NewTarget();

        var sourceDb = OpenSource(app, out var scope);
        using (scope)
        {
            // SQLite stores what the model's maximum length would refuse elsewhere, and is case-sensitive here.
            sourceDb.Set<AbuseReport>().Add(new AbuseReport
            {
                PackageId = "Copy.One", PackageVersion = "1.0.0", Reason = new string('x', 200), Details = "d", Created = DateTime.UtcNow,
            });
            sourceDb.Set<Organization>().Add(new Organization { Name = "ACME", Created = DateTime.UtcNow });
            await sourceDb.SaveChangesAsync(cancellationToken);
        }

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        var status = await WaitAsync(app);

        Assert.True(status.Phase == DatabaseCopyPhase.Succeeded, string.Join(" ", status.Errors));
        using var targetDb = OpenTarget(target);
        Assert.Equal(200, (await targetDb.Set<AbuseReport>().SingleAsync(cancellationToken)).Reason!.Length);
        Assert.Equal(2, await targetDb.Set<Organization>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task ASourceRefusalHappensBeforeTheTargetIsCreatedOrMigrated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync();
        var file = Path.Combine(_directory, "not-yet.db");
        var target = $"Data Source={file}";

        var sourceDb = OpenSource(app, out var scope);
        using (scope)
        {
            await sourceDb.Database.ExecuteSqlRawAsync(
                "DELETE FROM __EFMigrationsHistory WHERE MigrationId = (SELECT MAX(MigrationId) FROM __EFMigrationsHistory)", cancellationToken);
        }

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        var status = await WaitAsync(app);

        Assert.Equal(DatabaseCopyPhase.Failed, status.Phase);
        Assert.Contains(status.Errors, e => e.Contains("pending migration"));

        // Not even connected to: a SQLite file is created by the first connection.
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task ANonEmptyTargetWithAnOlderSchemaSaysItsSchemaWasUpdated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync();
        var target = NewTarget();
        using (var targetDb = OpenTarget(target))
        {
            // An older schema: migrated up to the accounts only, so the target has migrations still to apply.
            await Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
                .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(targetDb)
                .MigrateAsync("20261017012801_AddAccounts", cancellationToken);
            targetDb.Set<Organization>().Add(new Organization { Name = "Existing", Created = DateTime.UtcNow });
            await targetDb.SaveChangesAsync(cancellationToken);
            Assert.NotEmpty(await targetDb.Database.GetPendingMigrationsAsync(cancellationToken));
        }

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        var status = await WaitAsync(app);

        Assert.Equal(DatabaseCopyPhase.Failed, status.Phase);
        Assert.Contains(status.Errors, e => e.Contains("not empty") && e.Contains("schema was updated") && !e.Contains("Nothing was changed"));
    }

    [Fact]
    public async Task ASourceWithPendingMigrationsIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync();
        var target = NewTarget();

        var sourceDb = OpenSource(app, out var scope);
        using (scope)
        {
            await sourceDb.Database.ExecuteSqlRawAsync(
                "DELETE FROM __EFMigrationsHistory WHERE MigrationId = (SELECT MAX(MigrationId) FROM __EFMigrationsHistory)", cancellationToken);
        }

        using (await PostDatabaseAsync(admin, "Sqlite", target)) { }
        var status = await WaitAsync(app);

        Assert.Equal(DatabaseCopyPhase.Failed, status.Phase);
        Assert.Contains(status.Errors, e => e.Contains("pending migration"));
        Assert.False(app.Services.GetRequiredService<MaintenanceGate>().IsClosed);
    }

    [Fact]
    public async Task AzureTableTargetOffersNoCopyAndSavesAsBefore()
    {
        var (app, admin) = await StartAsync();

        using var response = await PostDatabaseAsync(admin, "AzureTable", "UseDevelopmentStorage=true");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(DatabaseCopyPhase.Idle, app.Services.GetRequiredService<DatabaseCopyService>().Status.Phase);
        Assert.Equal("AzureTable", SavedSettings(app)["Database:Type"]);
        Assert.False(app.Services.GetRequiredService<MaintenanceGate>().IsClosed);
    }

    [Fact]
    public async Task TheOptionIsOfferedForARelationalSourceAndHiddenOtherwise()
    {
        var (_, admin) = await StartAsync();
        var html = await admin.GetPageAsync("admin/settings");

        Assert.Contains("id=\"copy-option\"", html);
        Assert.Contains("name=\"copyData\"", html);
        Assert.Contains("data-target-types=\"MySql,PostgreSql,Sqlite,SqlServer\"", html, StringComparison.OrdinalIgnoreCase);

        // A running database that is not relational (AzureTable registers no factory) has nothing to copy from.
        var (nonRelational, nonRelationalAdmin) = await StartAsync(services =>
            services.RemoveAll<IDatabaseContextFactory>());
        var plain = await nonRelationalAdmin.GetPageAsync("admin/settings");
        Assert.DoesNotContain("id=\"copy-option\"", plain);

        using var response = await PostDatabaseAsync(nonRelationalAdmin, "Sqlite", NewTarget());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(DatabaseCopyPhase.Idle, nonRelational.Services.GetRequiredService<DatabaseCopyService>().Status.Phase);
    }

    [Fact]
    public async Task WithoutTheOptionTheSettingsAreSavedAtOnceWithoutCopying()
    {
        var (app, admin) = await StartAsync();
        var target = NewTarget();

        using var response = await PostDatabaseAsync(admin, "Sqlite", target, copyData: false);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(DatabaseCopyPhase.Idle, app.Services.GetRequiredService<DatabaseCopyService>().Status.Phase);
        Assert.Equal(target, SavedSettings(app)["Database:ConnectionString"]);
    }

    [Fact]
    public void TablesAreOrderedAfterTheOnesTheyReference()
    {
        using var context = OpenTarget("Data Source=:memory:");
        var tables = DatabaseTable.InDependencyOrder(context.Model);
        var names = tables.Select(t => t.EntityType.Name).ToList();

        Assert.Contains("BaGet.Core.Package", names);
        Assert.Contains("BaGet.Core.BaGetUser", names);
        Assert.Contains(names, n => n.Contains("IdentityUserRole"));
        Assert.Equal(names.Count, names.Distinct().Count());

        foreach (var table in tables)
        {
            foreach (var foreignKey in table.EntityType.GetForeignKeys().Where(f => f.PrincipalEntityType != table.EntityType))
            {
                Assert.True(
                    names.IndexOf(foreignKey.PrincipalEntityType.Name) < names.IndexOf(table.EntityType.Name),
                    $"{table.EntityType.Name} comes before {foreignKey.PrincipalEntityType.Name}");
            }
        }
    }

    [Fact]
    public void PreflightRefusesLocalDatesOnlyForPostgreSql()
    {
        using var context = OpenTarget("Data Source=:memory:");
        var table = DatabaseTable.InDependencyOrder(context.Model).Single(t => t.EntityType.ClrType == typeof(Organization));
        var rows = new object[] { new Organization { Key = 1, Name = "a", Created = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local) } };

        var postgres = new DatabaseCopyPreflight("PostgreSql", DesignModel(context));
        postgres.Inspect(table, rows);
        Assert.Contains(postgres.Problems, p => p.Contains("local time"));

        var other = new DatabaseCopyPreflight("SqlServer", DesignModel(context));
        other.Inspect(table, rows);
        Assert.Empty(other.Problems);
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IModel DesignModel(DbContext context)
        => Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(context).Model;

    private static DatabaseTable TableOf<T>(DbContext context)
        => DatabaseTable.InDependencyOrder(context.Model).Single(t => t.EntityType.ClrType == typeof(T));

    [Fact]
    public void PreflightCaseDuplicatesAreJudgedOnTheTargetColumnsCollation()
    {
        using var context = OpenTarget("Data Source=:memory:");
        var organizations = TableOf<Organization>(context);
        var rows = new object[]
        {
            new Organization { Key = 1, Name = "Acme" },
            new Organization { Key = 2, Name = "acme" },
        };

        // Default collations: PostgreSQL and SQLite compare case-sensitively, SQL Server and MySQL do not.
        foreach (var type in new[] { "PostgreSql", "Sqlite" })
        {
            var sensitive = new DatabaseCopyPreflight(type, DesignModel(context));
            sensitive.Inspect(organizations, rows);
            Assert.Empty(sensitive.Problems);
        }

        foreach (var type in new[] { "MySql", "SqlServer" })
        {
            var insensitive = new DatabaseCopyPreflight(type, DesignModel(context));
            insensitive.Inspect(organizations, rows);
            Assert.Single(insensitive.Problems);
        }

        // A column the model types NOCASE (or citext) is case-insensitive whatever the engine's default.
        var packages = TableOf<Package>(context);
        var packageRows = new object[]
        {
            new Package { Key = 1, Id = "Foo", NormalizedVersionString = "1.0.0" },
            new Package { Key = 2, Id = "foo", NormalizedVersionString = "1.0.0" },
        };
        foreach (var type in new[] { "Sqlite", "PostgreSql" })
        {
            var nocase = new DatabaseCopyPreflight(type, DesignModel(context));
            nocase.Inspect(packages, packageRows);
            Assert.Single(nocase.Problems);
        }
    }

    [Fact]
    public void PreflightCaseDuplicatesInAKeyAreFoundNotOnlyInAnIndex()
    {
        using var context = OpenTarget("Data Source=:memory:");
        var logins = DatabaseTable.InDependencyOrder(context.Model).Single(t => t.EntityType.ClrType.Name.StartsWith("IdentityUserLogin"));
        var rows = new object[]
        {
            new Microsoft.AspNetCore.Identity.IdentityUserLogin<string> { LoginProvider = "Google", ProviderKey = "abc", UserId = "u1" },
            new Microsoft.AspNetCore.Identity.IdentityUserLogin<string> { LoginProvider = "google", ProviderKey = "ABC", UserId = "u2" },
        };

        var mysql = new DatabaseCopyPreflight("MySql", DesignModel(context));
        mysql.Inspect(logins, rows);
        Assert.Single(mysql.Problems);
        Assert.Contains("differ only by case", mysql.Problems[0]);

        var sqlite = new DatabaseCopyPreflight("Sqlite", DesignModel(context));
        sqlite.Inspect(logins, rows);
        Assert.Empty(sqlite.Problems);
    }

    [Fact]
    public void PreflightMaximumLengthsAreCheckedOnlyForEnginesThatEnforceThem()
    {
        using var context = OpenTarget("Data Source=:memory:");
        var reports = TableOf<AbuseReport>(context);
        var rows = new object[]
        {
            new AbuseReport { Key = 1, PackageId = "a", PackageVersion = "1.0.0", Reason = new string('x', 200), Details = "d" },
        };

        foreach (var type in new[] { "SqlServer", "PostgreSql", "MySql" })
        {
            var enforcing = new DatabaseCopyPreflight(type, DesignModel(context));
            enforcing.Inspect(reports, rows);
            Assert.Contains(enforcing.Problems, p => p.Contains("AbuseReports.Reason") && p.Contains("200 characters") && p.Contains("128"));
        }

        var sqlite = new DatabaseCopyPreflight("Sqlite", DesignModel(context));
        sqlite.Inspect(reports, rows);
        Assert.Empty(sqlite.Problems);
    }

    [Fact]
    public void ErrorTextIsScrubbedOfTheConnectionStringAndItsCredentials()
    {
        const string connection = "Server=db.internal;Database=baget;User Id=svc;Password=hunter2;";

        var scrubbed = DatabaseCopyService.Scrub($"Login failed for hunter2 using {connection}", connection);
        Assert.DoesNotContain("hunter2", scrubbed);
        Assert.DoesNotContain("db.internal", scrubbed);

        var partial = DatabaseCopyService.Scrub("Access denied for user 'svc' (password hunter2)", connection);
        Assert.DoesNotContain("hunter2", partial);
        Assert.DoesNotContain("svc", partial);
    }
}
