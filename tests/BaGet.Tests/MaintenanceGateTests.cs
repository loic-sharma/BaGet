using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Validation.Pipeline;
using BaGet.Validation.Queue;

namespace BaGet.Tests;

/// <summary>
/// While the <see cref="MaintenanceGate"/> is closed every database write is refused (HTTP 503 with
/// <c>Retry-After</c>), reads and downloads keep working, a context marked with
/// <see cref="MaintenanceGate.AllowWrites(DbContextOptionsBuilder)"/> can still write, and background
/// workers log and retry instead of crashing or quarantining work.
/// </summary>
public class MaintenanceGateTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public MaintenanceGateTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables)) disposable.Dispose();
    }

    private BaGetApplication Start(Dictionary<string, string>? config = null, Action<IServiceCollection>? configureServices = null)
    {
        var settings = new Dictionary<string, string> { { "Accounts:Enabled", "true" } };
        foreach (var pair in config ?? new Dictionary<string, string>()) settings[pair.Key] = pair.Value;

        var app = new BaGetApplication(_output, configOverrides: settings, configureServices: configureServices);
        _disposables.Add(app);
        return app;
    }

    private static MaintenanceGate GateOf(BaGetApplication app) => app.Services.GetRequiredService<MaintenanceGate>();

    private static async Task<string> CreateKeyAsync(BaGetApplication app, string userName)
    {
        var user = await AdminTestSupport.FindAsync(app, userName);
        using var scope = app.Services.CreateScope();
        #pragma warning disable CS8602
        return await scope.ServiceProvider.GetRequiredService<ApiKeyService>().CreateAsync(
            user.Id, "test", new[] { ApiKeyScopes.Push, ApiKeyScopes.Unlist }, "*", null, CancellationToken.None);
        #pragma warning restore CS8602
    }

    private static async Task<HttpResponseMessage> PushAsync(HttpClient client, string id, string version, string key)
    {
        using var package = TestPackages.Build(id, version);
        using var request = TestPackages.PushRequest(package, key);
        return await client.SendAsync(request);
    }

    private static void AssertRefused(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.RetryAfter != null, "Retry-After is missing.");
    }

    [Fact]
    public async Task PushDeleteSignUpAndSettingsSaveAreRefusedWith503WhileClosed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var app = Start();
        await AdminTestSupport.CreateUserAsync(app, "root", administrator: true);
        await AdminTestSupport.PushAsync(app, "root", "Existing.Package");
        var key = await CreateKeyAsync(app, "root");
        var client = app.CreateClient();
        _disposables.Add(client);
        var admin = new Browser(app);
        _disposables.Add(admin);
        await AdminTestSupport.SignInAsync(admin, "root");

        GateOf(app).Close("Copying the database");

        using (var push = await PushAsync(client, "Refused.Package", "1.0.0", key)) AssertRefused(push);

        using (var delete = new HttpRequestMessage(HttpMethod.Delete, "api/v2/package/Existing.Package/1.0.0"))
        {
            delete.Headers.Add("X-NuGet-ApiKey", key);
            using var response = await client.SendAsync(delete, cancellationToken);
            AssertRefused(response);
        }

        using (var signUp = await new Browser(app).PostAsync("account/register", new Dictionary<string, string>
               {
                   { "UserName", "newcomer" },
                   { "Password", "Passw0rd!" },
                   { "ConfirmPassword", "Passw0rd!" },
               }))
        {
            AssertRefused(signUp);
            Assert.Contains("Copying the database", await signUp.Content.ReadAsStringAsync(cancellationToken));
        }

        using (var settings = await admin.PostAsync("admin/settings", new Dictionary<string, string>
               {
                   { "section", "" },
                   { "password", AdminTestSupport.Password },
                   { "v:AllowPackageOverwrites", "true" },
               }))
        {
            AssertRefused(settings);
        }

        // Nothing changed: the refused push left no stored content behind either, so it works once reopened.
        Assert.Null(await AdminTestSupport.FindAsync(app, "newcomer"));
        Assert.True(GateOf(app).Open());
        using var retry = await PushAsync(client, "Refused.Package", "1.0.0", key);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task ReadsDownloadsAndPagesWorkWhileClosedAndPagesShowABanner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var app = Start();
        await AdminTestSupport.CreateUserAsync(app, "root", administrator: true);
        await AdminTestSupport.PushAsync(app, "root", "Served.Package");
        var client = app.CreateClient();
        _disposables.Add(client);

        GateOf(app).Close("Copying the database");

        using (var index = await client.GetAsync("v3/index.json", cancellationToken)) Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        using (var search = await client.GetAsync("v3/search?q=Served", cancellationToken)) Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using (var download = await client.GetAsync("v3/package/served.package/1.0.0/served.package.1.0.0.nupkg", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        }

        using var home = await client.GetAsync("", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var html = await home.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("id=\"maintenance-banner\"", html);
        Assert.Contains("Copying the database", html);

        var package = (await AdminTestSupport.VersionsAsync(app, "Served.Package")).Single();
        Assert.Equal(0, package.Downloads);

        GateOf(app).Open();
        Assert.DoesNotContain("maintenance-banner", await client.GetStringAsync("", cancellationToken));
    }

    [Fact]
    public void ClosingUntilRestartCannotBeReopened()
    {
        var gate = new MaintenanceGate();
        Assert.False(gate.IsClosed);

        gate.Close("Copying");
        Assert.True(gate.IsClosed);
        Assert.False(gate.IsClosedUntilRestart);
        Assert.Equal("Copying", gate.Reason);
        Assert.True(gate.Open());
        Assert.False(gate.IsClosed);
        Assert.Null(gate.Reason);

        gate.CloseUntilRestart("Restart now");
        Assert.True(gate.IsClosedUntilRestart);
        Assert.False(gate.Open());
        Assert.True(gate.IsClosed);

        gate.Close("Other");
        Assert.Equal("Restart now", gate.Reason);
        Assert.Throws<MaintenanceException>(gate.ThrowIfClosed);
    }

    [Fact]
    public async Task AContextMarkedAllowWritesCanStillWriteWhileOthersCannot()
    {
        var gate = new MaintenanceGate();
        gate.Close("Copying");
        var interceptor = new MaintenanceSaveChangesInterceptor(gate);

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        DbContextOptions<SqliteContext> Options(bool allowWrites)
        {
            var builder = new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).AddInterceptors(interceptor);
            if (allowWrites) MaintenanceGate.AllowWrites(builder);
            return builder.Options;
        }

        using (var target = new SqliteContext(Options(allowWrites: true)))
        {
            await target.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            target.Organizations!.Add(new Organization { Name = "copied", Created = DateTime.UtcNow });
            Assert.Equal(1, await target.SaveChangesAsync(TestContext.Current.CancellationToken));
            target.Organizations.Add(new Organization { Name = "copied2", Created = DateTime.UtcNow });
            Assert.Equal(1, target.SaveChanges());
        }

        using (var other = new SqliteContext(Options(allowWrites: false)))
        {
            other.Organizations!.Add(new Organization { Name = "refused", Created = DateTime.UtcNow });
            await Assert.ThrowsAsync<MaintenanceException>(() => other.SaveChangesAsync(TestContext.Current.CancellationToken));
            Assert.Throws<MaintenanceException>(() => other.SaveChanges());
            Assert.Equal(2, await other.Organizations.CountAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task AValidatedPushRefusedWhileClosedStagesNoFileAndWorksOnceReopened()
    {
        var storage = Path.Combine(Path.GetTempPath(), "baget-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            var app = Start(new Dictionary<string, string>
            {
                { "Validation:Enabled", "true" },
                { "Accounts:Enabled", "false" },
                { "Storage:Path", storage },
            });
            var client = app.CreateClient();
            _disposables.Add(client);

            GateOf(app).Close("Copying the database");
            using (var push = await PushAsync(client, "Staged.Package", "1.0.0", key: null!)) AssertRefused(push);

            // Refused before the staging write: no orphan "validation/{guid}.nupkg" is left behind.
            Assert.Empty(Directory.Exists(storage) ? Directory.GetFiles(storage, "*", SearchOption.AllDirectories) : Array.Empty<string>());

            Assert.True(GateOf(app).Open());
            using var retry = await PushAsync(client, "Staged.Package", "1.0.0", key: null!);
            Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        }
        finally
        {
            try { Directory.Delete(storage, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task ACatalogEntryRefusedWhileClosedWritesNoLeafFile()
    {
        var storage = Path.Combine(Path.GetTempPath(), "baget-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            var app = Start(new Dictionary<string, string> { { "Accounts:Enabled", "false" }, { "Storage:Path", storage } });
            var client = app.CreateClient();
            _disposables.Add(client);
            using (var push = await PushAsync(client, "Cataloged.Package", "1.0.0", key: null!)) Assert.Equal(HttpStatusCode.Created, push.StatusCode);

            var before = Directory.GetFiles(storage, "*", SearchOption.AllDirectories).Length;
            Assert.Contains(Directory.GetFiles(storage, "*.json", SearchOption.AllDirectories), f => f.Contains("catalog"));

            GateOf(app).Close("Copying the database");
            using var scope = app.Services.CreateScope();
            var writer = scope.ServiceProvider.GetRequiredService<BaGet.Catalog.Storage.CatalogWriter>();
            await Assert.ThrowsAsync<MaintenanceException>(() => writer.OnPackageChangedAsync(
                "Cataloged.Package", NuGet.Versioning.NuGetVersion.Parse("1.0.0"), CancellationToken.None));

            // Refused before the leaf was written, so nothing is orphaned.
            Assert.Equal(before, Directory.GetFiles(storage, "*", SearchOption.AllDirectories).Length);
        }
        finally
        {
            try { Directory.Delete(storage, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task TheValidationWorkerSurvivesAClosedGateAndFinishesOnceItReopens()
    {
        var previous = ValidationWorker.MaintenanceRetryDelay;
        ValidationWorker.MaintenanceRetryDelay = TimeSpan.FromMilliseconds(100);
        try
        {
            var validator = new BlockingValidator();
            var app = Start(
                new Dictionary<string, string> { { "Validation:Enabled", "true" }, { "Accounts:Enabled", "false" } },
                services => services.AddSingleton<IPackageValidator>(validator));
            var client = app.CreateClient();
            _disposables.Add(client);

            using (var push = await PushAsync(client, "Late.Package", "1.0.0", key: null!))
            {
                Assert.Equal(HttpStatusCode.Accepted, push.StatusCode);
            }

            await validator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            // The validators finish while the database is read-only: the worker must neither crash nor quarantine.
            GateOf(app).Close("Copying the database");
            validator.Release.SetResult(true);
            await Task.Delay(500, TestContext.Current.CancellationToken);
            Assert.Equal("Validating", await StatusAsync(client, "Late.Package", "1.0.0"));

            GateOf(app).Open();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            string? status;
            while ((status = await StatusAsync(client, "Late.Package", "1.0.0")) == "Validating" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            Assert.Equal("Available", status);
        }
        finally
        {
            ValidationWorker.MaintenanceRetryDelay = previous;
        }
    }

    private static async Task<string?> StatusAsync(HttpClient client, string id, string version)
    {
        using var status = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync($"v3/package/{id}/{version}/validation"));
        return status.RootElement.GetProperty("status").GetString();
    }

    private class BlockingValidator : IPackageValidator
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "Block";

        public async Task<PackageValidatorResult> ValidateAsync(PackageValidationContext context, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return PackageValidatorResult.Valid;
        }
    }
}
