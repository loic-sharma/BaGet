using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The host logs through Serilog, configured from the "Serilog" section; libraries keep using
/// <see cref="ILogger{TCategoryName}"/>.
/// </summary>
public class LoggingTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public LoggingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void LoggersAreBackedBySerilog()
    {
        var app = Start();

        var factory = app.Services.GetRequiredService<ILoggerFactory>();
        var logger = app.Services.GetRequiredService<ILogger<LoggingTests>>();

        Assert.Equal("Serilog.Extensions.Logging.SerilogLoggerFactory", factory.GetType().FullName);
        Assert.NotNull(logger);
    }

    [Fact]
    public void TheConfiguredMinimumLevelIsHonoured()
    {
        var logs = new CapturingLoggerProvider();
        var app = Start(logs, new Dictionary<string, string> { { "Serilog:MinimumLevel:Default", "Warning" } });
        var logger = app.Services.GetRequiredService<ILogger<LoggingTests>>();

        logger.LogInformation("below the minimum");
        logger.LogWarning("at the minimum");

        Assert.DoesNotContain("below the minimum", logs.Messages);
        Assert.Contains("at the minimum", logs.Messages);
    }

    [Fact]
    public void AnOverrideBeatsTheDefaultForItsNamespace()
    {
        var logs = new CapturingLoggerProvider();
        var app = Start(logs, new Dictionary<string, string>
        {
            { "Serilog:MinimumLevel:Default", "Warning" },
            { "Serilog:MinimumLevel:Override:BaGet.Tests", "Debug" },
        });
        var logger = app.Services.GetRequiredService<ILogger<LoggingTests>>();

        logger.LogDebug("verbose for the namespace");

        Assert.Contains("verbose for the namespace", logs.Messages);
    }

    [Fact]
    public async Task TheHarnessWritesNoLogFileAnywhere()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var app = Start();
        var root = app.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;
        var temp = Path.GetDirectoryName(
            app.Services.GetRequiredService<IConfiguration>()["Database:ConnectionString"]!.Replace("Data Source=", ""))!;
        var configuration = app.Services.GetRequiredService<IConfiguration>();
        var before = LogFiles(root, temp);

        // The harness only says "Enabled=false"; it is the host that blanks the sinks' names.
        Assert.Equal("", configuration["Serilog:WriteTo:File:Name"]);
        Assert.Equal("", configuration["Serilog:WriteTo:Console:Name"]);

        using var client = app.CreateClient();
        (await client.GetAsync("v3/index.json", cancellationToken)).EnsureSuccessStatusCode();
        app.Dispose();

        // Whatever the timestamps: no log file was created or grown under the content root or the temp directory.
        Assert.Equal(before, LogFiles(root, temp));
        Assert.Empty(LogFiles(temp));
    }

    private static Dictionary<string, long> LogFiles(params string[] directories) =>
        directories
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.log", SearchOption.AllDirectories))
            .Distinct()
            .ToDictionary(f => f, f => new FileInfo(f).Length);

    [Fact]
    public async Task TheTokenOfAnNpmLogoutIsNotLoggedInThePath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var logs = new CapturingLoggerProvider();
        var app = Start(logs);
        await app.CreateNpmRepositoryAsync("web", cancellationToken: cancellationToken);
        using var client = app.CreateClient();

        using var response = await client.DeleteAsync("r/web/npm/-/user/token/PATH-SECRET", cancellationToken);

        var line = Assert.Single(logs.Messages.Where(m => m.Contains("responded")).Distinct());
        Assert.Contains("DELETE", line);
        Assert.Contains("/r/web/npm/-/user/token/Redacted", line);
        Assert.DoesNotContain(logs.Messages, m => m.Contains("PATH-SECRET"));
    }

    [Fact]
    public async Task ARequestIsLoggedOnceWithoutItsQueryStringOrApiKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var logs = new CapturingLoggerProvider();
        var app = Start(logs);
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "v3/index.json?apikey=QUERY-SECRET&token=QUERY-SECRET");
        request.Headers.Add("X-NuGet-ApiKey", "HEADER-SECRET");

        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
        // With writeToProviders Serilog hands each event to a registered ILoggerProvider twice, so count
        // distinct lines; the sinks (console, file) write it once.
        var lines = logs.Messages.Where(m => m.Contains("responded")).Distinct().ToList();
        var line = Assert.Single(lines);
        Assert.Contains("GET", line);
        Assert.Contains("/v3/index.json", line);
        Assert.Contains("200", line);
        Assert.DoesNotContain(logs.Messages, m => m.Contains("SECRET") || m.Contains("apikey=", StringComparison.OrdinalIgnoreCase) || m.Contains("?"));
    }

    private BaGetApplication Start(CapturingLoggerProvider? logs = null, Dictionary<string, string>? settings = null)
    {
        var app = new BaGetApplication(
            _output,
            configOverrides: settings,
            configureServices: logs == null ? null : s => s.AddSingleton<ILoggerProvider>(logs));
        _disposables.Add(app);
        return app;
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capture(this);

        public void Dispose() { }

        private sealed class Capture : ILogger
        {
            private readonly CapturingLoggerProvider _owner;

            public Capture(CapturingLoggerProvider owner) => _owner = owner;

            #pragma warning disable CS8633
            public IDisposable? BeginScope<TState>(TState state) => null;
            #pragma warning restore CS8633

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_owner.Messages)
                {
                    _owner.Messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
