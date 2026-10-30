using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BaGet.Tests;

public class BaGetApplication : WebApplicationFactory<Program>
{
    private readonly ITestOutputHelper _output;
    private readonly HttpClient? _upstreamClient;
    private readonly Dictionary<string, string>? _configOverrides;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly string _environment;

    /// <param name="environment">
    /// "Production" by default. "Development" serves the Razor class libraries' static web
    /// assets from the build output, as running from Visual Studio does.
    /// </param>
    public BaGetApplication(
        ITestOutputHelper output,
        HttpClient? upstreamClient = null,
        Dictionary<string, string>? configOverrides = null,
        Action<IServiceCollection>? configureServices = null,
        string environment = "Production")
    {
        _configureServices = configureServices;
        _environment = environment;
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _upstreamClient = upstreamClient;
        _configOverrides = configOverrides;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Create temporary storage paths.
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "BaGetTests",
            Guid.NewGuid().ToString("N"));
        var sqlitePath = Path.Combine(tempPath, "BaGet.db");
        var storagePath = Path.Combine(tempPath, "Packages");

        Directory.CreateDirectory(tempPath);

        // The shipped appsettings.json turns telemetry export on (Jaeger on localhost:4317). A test host must start with it off, but a
        // startup key (UseSetting) would shadow what a telemetry test saves through SettingsService, and a seeded overrides file would
        // change the Settings page ("reset to default" boxes). So the default is an in-memory source placed right below the overrides
        // file: it outranks appsettings.json, a save outranks it, and nothing it holds shows as overridden.
        var overridesFile = SettingsOverridesStore.DefaultFileName;
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var index = configuration.Sources.ToList().FindIndex(s => s is JsonConfigurationSource json && json.Path == overridesFile);
            var source = new MemoryConfigurationSource
            {
                InitialData = new Dictionary<string, string?> { { "Telemetry:Enabled", "false" } },
            };
            configuration.Sources.Insert(index < 0 ? configuration.Sources.Count : index, source);
        });

        // Setup the integration test configuration.
        var settings = new Dictionary<string, string>
        {
            { "Database:Type", "Sqlite" },
            { "Database:ConnectionString", $"Data Source={sqlitePath}" },
            { "Storage:Type", "FileSystem" },
            { "Storage:Path", storagePath },
            { "Search:Type", "Database" },
            { "Accounts:Enabled", "false" },
            // WebApplicationFactory runs Main, whose startup would otherwise create "Admin" in every
            // accounts-enabled test; tests that need it call AdministratorService directly.
            { "Accounts:CreateDefaultAdministrator", "false" },
            { SettingsOverridesStore.PathKey, Path.Combine(tempPath, SettingsOverridesStore.DefaultFileName) },
            { "Mirror:Enabled", _upstreamClient != null ? "true": "false" },
            { "Mirror:PackageSource", "http://localhost/v3/index.json" },
            // The shipped Serilog sinks (console, logs/baget-.log, Windows Event Log) are switched off:
            // events reach the xunit output through XunitLoggerProvider instead (Serilog is built with
            // writeToProviders). "Enabled=false" is the documented, cross-platform switch.
            { "Serilog:WriteTo:Console:Enabled", "false" },
            { "Serilog:WriteTo:File:Enabled", "false" },
            { "Serilog:EventLog:Enabled", "false" },
        };

        foreach (var pair in _configOverrides ?? new Dictionary<string, string>())
        {
            settings[pair.Key] = pair.Value;
        }

        // These are applied with UseSetting rather than ConfigureAppConfiguration so
        // that they are in place before the application registers its services. Under
        // the minimal hosting model the entry point reads builder.Configuration while
        // registering - AddBaGetAccounts decides from "Accounts:Enabled", for one - and
        // a provider appended afterwards would arrive too late for those reads.
        foreach (var pair in settings)
        {
            builder.UseSetting(pair.Key, pair.Value);
        }

        builder
            .UseEnvironment(_environment)
            .ConfigureLogging(logging =>
            {
                // BaGet uses console logging by default. This logger throws operation
                // cancelled exceptions when the host shuts down, causing the the debugger
                // to pause repeatedly if CLR exceptions are enabled.
                logging.ClearProviders();

                // Pipe logs to the xunit output.
                logging.AddProvider(new XunitLoggerProvider(_output));
            })
            .ConfigureServices(services => { })
            .ConfigureServices((context, services) =>
            {
                // Make time deterministic for testing purposes.
                var time = new Mock<SystemTime>();
                time
                    .Setup(t => t.UtcNow)
                    // DateTime.Parse converts a "Z" timestamp to local time, so the
                    // mock would return a local DateTime from a property that is
                    // documented, and implemented, as returning UTC.
                    .Returns(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

                services.AddSingleton(time.Object);

                // No real SMTP in tests: whatever would be mailed is captured instead.
                services.RemoveAll<IEmailSender<BaGetUser>>();
                services.AddSingleton<CapturingEmailSender>();
                services.AddSingleton<IEmailSender<BaGetUser>>(p => p.GetRequiredService<CapturingEmailSender>());
                services.RemoveAll<IEmailMessageSender>();
                services.AddSingleton<IEmailMessageSender>(p => p.GetRequiredService<CapturingEmailSender>());
                _configureServices?.Invoke(services);
                if (_upstreamClient != null)
                {
                    services.AddSingleton(_upstreamClient);
                    services.AddSingleton<IRemoteHttpClientFactory>(new ForwardingRemoteHttpClientFactory(_upstreamClient));
                }

                // Setup the integration test database.
                var provider = services.BuildServiceProvider();
                var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

                // Ensure the database is created before we run migrations. The migrations
                // can create the database too, however, migrations check whether the database exists
                // first. The SQLite provider implements this by attempting to open a connection,
                // and if that fails, creating the database. This throws several exceptions that
                // pauses the debugger repeatedly if CLR exceptions are enabled.
                // See: https://github.com/dotnet/efcore/blob/644d3c8c3a604fd0121d90eaf34f14870e19bcff/src/EFCore.Sqlite.Core/Storage/Internal/SqliteDatabaseCreator.cs#L88-L98
                using var scope = scopeFactory.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<IContext>();
                var dbCreator = ctx.Database.GetService<IRelationalDatabaseCreator>();

                dbCreator.Create();
                ctx.Database.Migrate();
            });
    }
}

internal static class BaGetWebApplicationFactoryExtensions
{
    /// <summary>Creates a local NuGet repository with the given name.</summary>
    public static async Task CreateRepositoryAsync(
        this WebApplicationFactory<Program> factory,
        string name,
        CancellationToken cancellationToken = default)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.NuGet, RepositoryKind.Local, cancellationToken);
    }

    /// <summary>Creates a local npm repository with the given name (its registry is at <c>/r/{name}/npm/</c>).</summary>
    public static async Task CreateNpmRepositoryAsync(
        this WebApplicationFactory<Program> factory,
        string name,
        bool anonymousRead = true,
        CancellationToken cancellationToken = default)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.Npm, RepositoryKind.Local, new Dictionary<string, string?>(), anonymousRead, cancellationToken);
    }

    /// <summary>Creates a local Docker repository with the given name (its registry is at <c>/v2/{name}/…</c>).</summary>
    public static async Task CreateDockerRepositoryAsync(
        this WebApplicationFactory<Program> factory,
        string name,
        bool anonymousRead = true,
        CancellationToken cancellationToken = default)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.Docker, RepositoryKind.Local, new Dictionary<string, string?>(), anonymousRead, cancellationToken);
    }

    /// <summary>
    /// Creates a local Maven repository with the given name (its repository is at <c>/r/{name}/maven/</c>); the settings are the
    /// create form's fields (<c>policy</c>, <c>redeploy</c>, <c>snapshotBuilds</c>).
    /// </summary>
    public static async Task CreateMavenRepositoryAsync(
        this WebApplicationFactory<Program> factory,
        string name,
        string? policy = null,
        bool redeploy = false,
        int snapshotBuilds = 0,
        bool anonymousRead = true,
        CancellationToken cancellationToken = default)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(
                name,
                RepositoryFormat.Maven,
                RepositoryKind.Local,
                new Dictionary<string, string?>
                {
                    ["policy"] = policy,
                    ["redeploy"] = redeploy ? "Yes" : "No",
                    ["snapshotBuilds"] = snapshotBuilds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                anonymousRead,
                cancellationToken);
    }

    /// <summary>A DI scope whose current repository is the named one (null = default), as routing sets it.</summary>
    public static async Task<IServiceScope> CreateRepositoryScopeAsync(
        this WebApplicationFactory<Program> factory,
        string? repository,
        CancellationToken cancellationToken = default)
    {
        var scope = factory.Services.CreateScope();
        if (repository != null)
        {
            var found = await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .FindAsync(repository, cancellationToken);
            scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use(found
                ?? throw new InvalidOperationException($"Repository '{repository}' does not exist."));
        }

        return scope;
    }

    public static async Task AddPackageAsync(
        this WebApplicationFactory<Program> factory,
        Stream? package,
        CancellationToken cancellationToken = default,
        string? repository = null)
    {
        using var scope = await factory.CreateRepositoryScopeAsync(repository, cancellationToken);
        var indexer = scope.ServiceProvider.GetRequiredService<IPackageIndexingService>();

        var result = await indexer.IndexAsync(package, cancellationToken);
        if (result != PackageIndexingResult.Success)
        {
            throw new InvalidOperationException($"Unexpected indexing result {result}");
        }
    }

    public static async Task AddSymbolPackageAsync(
        this WebApplicationFactory<Program> factory,
        Stream? symbolPackage,
        CancellationToken cancellationToken = default,
        string? repository = null)
    {
        using var scope = await factory.CreateRepositoryScopeAsync(repository, cancellationToken);
        var indexer = scope.ServiceProvider.GetRequiredService<ISymbolIndexingService>();

        var result = await indexer.IndexAsync(symbolPackage, cancellationToken);
        if (result != SymbolIndexingResult.Success)
        {
            throw new InvalidOperationException($"Unexpected indexing result {result}");
        }
    }
}