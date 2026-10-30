using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// How the host turns the "Serilog" configuration into a logger: the code-set level floor, file path
/// resolution, the <c>Enabled</c> switch, live level reload and the Windows Event Log sink.
/// </summary>
public class LoggingConfigurationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();
    private readonly List<string> _directories = new();

    public LoggingConfigurationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void WithoutAnyConfiguredLevelsRequestUrlsAreNotLogged()
    {
        var sink = Build(new Dictionary<string, string?>());

        Log(sink, "Microsoft.AspNetCore.Hosting.Diagnostics", "Request starting GET /x?apikey=SECRET");
        Log(sink, "Microsoft.AspNetCore.Routing", "routing noise");
        Log(sink, "Microsoft.Hosting.Lifetime", "Now listening on");
        Log(sink, "BaGet.Core.Whatever", "app information");

        Assert.Equal(new[] { "Now listening on", "app information" }, sink.Messages);
    }

    [Fact]
    public void ConfiguringOnlyTheDefaultKeepsTheDiagnosticsFloor()
    {
        var sink = Build(new Dictionary<string, string?> { { "Serilog:MinimumLevel:Default", "Debug" } });

        Log(sink, "Microsoft.AspNetCore.Hosting.Diagnostics", "Request starting ?apikey=SECRET", LogEventLevel.Information);
        Log(sink, "BaGet.Core.Whatever", "debug is now allowed", LogEventLevel.Debug);

        Assert.Equal(new[] { "debug is now allowed" }, sink.Messages);
    }

    [Fact]
    public void AnExplicitDiagnosticsOverrideIsTheOperatorsChoice()
    {
        var sink = Build(new Dictionary<string, string?>
        {
            { "Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Hosting.Diagnostics", "Information" },
        });

        Log(sink, "Microsoft.AspNetCore.Hosting.Diagnostics", "Request starting");

        Assert.Equal(new[] { "Request starting" }, sink.Messages);
    }

    [Fact]
    public void RelativeFileSinkPathsAreResolvedForEverySink()
    {
        var root = Path.Combine(Path.GetTempPath(), "BaGetRoot");
        var absolute = Path.Combine(Path.GetTempPath(), "elsewhere", "x-.log");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Serilog:WriteTo:File:Name", "File" },
            { "Serilog:WriteTo:File:Args:path", "logs/named-.log" },
            { "Serilog:WriteTo:0:Name", "file" },
            { "Serilog:WriteTo:0:Args:path", "logs/array-.log" },
            { "Serilog:WriteTo:1:Name", "File" },
            { "Serilog:WriteTo:1:Args:path", absolute },
            { "Serilog:WriteTo:2:Name", "Console" },
            { "Serilog:WriteTo:2:Args:path", "not-a-file" },
        });

        configuration.ResolveLogFilePaths(root);

        Assert.Equal(Path.Combine(root, "logs", "named-.log"), configuration["Serilog:WriteTo:File:Args:path"]);
        Assert.Equal(Path.Combine(root, "logs", "array-.log"), configuration["Serilog:WriteTo:0:Args:path"]);
        Assert.Equal(absolute, configuration["Serilog:WriteTo:1:Args:path"]);
        Assert.Equal("not-a-file", configuration["Serilog:WriteTo:2:Args:path"]);
    }

    [Fact]
    public void DisableSinksSwitchesOffSinksByNameOrIndex()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Serilog:WriteTo:Console:Name", "Console" },
            { "Serilog:WriteTo:Console:Enabled", "False" },
            { "Serilog:WriteTo:File:Name", "File" },
            { "Serilog:WriteTo:File:Enabled", "true" },
            { "Serilog:WriteTo:2:Name", "File" },
            { "Serilog:WriteTo:2:Enabled", "false" },
            { "Serilog:WriteTo:3:Name", "File" },
        });

        configuration.DisableSinks();

        Assert.Equal("", configuration["Serilog:WriteTo:Console:Name"]);
        Assert.Equal("File", configuration["Serilog:WriteTo:File:Name"]);
        Assert.Equal("", configuration["Serilog:WriteTo:2:Name"]);
        Assert.Equal("File", configuration["Serilog:WriteTo:3:Name"]);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("", true)]
    public async Task AFileSinkWritesUnlessEnabledIsFalse(string enabled, bool expectFile)
    {
        // The real entry point with command-line settings. (WebApplicationFactory's UseSetting gives every
        // parent key of a setting an empty value, which Serilog.Settings.Configuration reads as a sink name.)
        var directory = Path.Combine(Path.GetTempPath(), "BaGetLogTests", Guid.NewGuid().ToString("N"));
        _directories.Add(directory);
        var args = new List<string>
        {
            "--Serilog:WriteTo:File:Args:path=" + Path.Combine(directory, "baget-.log"),
            "--Serilog:WriteTo:Console:Enabled=false",
            "--Serilog:EventLog:Enabled=false",
            "--Database:Type=Sqlite",
            "--Database:ConnectionString=Data Source=" + Path.Combine(directory, "baget.db"),
            "--Storage:Type=FileSystem",
            "--Storage:Path=" + Path.Combine(directory, "packages"),
            "--Search:Type=Database",
            "--" + SettingsOverridesStore.PathKey + "=" + Path.Combine(directory, "overrides.json"),
        };
        if (enabled != "")
        {
            args.Add("--Serilog:WriteTo:File:Enabled=" + enabled);
        }

        var app = Program.BuildApplication(args.ToArray());
        app.Services.GetRequiredService<ILogger<LoggingConfigurationTests>>().LogInformation("a line for the file");
        await app.DisposeAsync();

        var files = Directory.GetFiles(directory, "baget-*.log");
        if (expectFile)
        {
            Assert.Contains("a line for the file", File.ReadAllText(Assert.Single(files)));
        }
        else
        {
            Assert.Empty(files);
        }
    }

    [Fact]
    public async Task MinimumLevelsReloadWithoutARestart()
    {
        var logs = new CapturingLoggerProvider();
        var app = Start(null, logs);
        var logger = app.Services.GetRequiredService<ILogger<LoggingConfigurationTests>>();
        var overrides = app.Services.GetRequiredService<IConfiguration>()[SettingsOverridesStore.PathKey]!;

        logger.LogInformation("before the change");
        File.WriteAllText(overrides, "{ \"Serilog\": { \"MinimumLevel\": { \"Default\": \"Warning\" } } }");

        // The file watcher fires asynchronously: probe until Information stops getting through.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var reloaded = false;
        for (var i = 0; DateTime.UtcNow < deadline && !reloaded; i++)
        {
            var probe = "probe " + i;
            logger.LogInformation(probe);
            await Task.Delay(250, TestContext.Current.CancellationToken);
            lock (logs.Messages) reloaded = !logs.Messages.Contains(probe);
        }

        logger.LogInformation("after the change");
        logger.LogWarning("warning after the change");

        Assert.True(reloaded, "the level change was not picked up");
        Assert.Contains("before the change", logs.Messages);
        Assert.DoesNotContain("after the change", logs.Messages);
        Assert.Contains("warning after the change", logs.Messages);
    }

    [Fact]
    public void WarningsReachTheWindowsApplicationLog()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows Event Log");
            return;
        }

        var marker = "BaGetLoggingTests" + Guid.NewGuid().ToString("N");
        var app = Start(new Dictionary<string, string> { { "Serilog:EventLog:Enabled", "true" } });
        var logger = app.Services.GetRequiredService<ILogger<LoggingConfigurationTests>>();

        logger.LogInformation("information {Marker}", marker);
        logger.LogWarning("warning {Marker}", marker);

        AssertSingleWarningEntry(marker);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AssertSingleWarningEntry(string marker)
    {
        using var log = new System.Diagnostics.EventLog("Application");
        var entries = log.Entries.Cast<System.Diagnostics.EventLogEntry>().Reverse().Take(300)
            .Where(e => e.Message.Contains(marker)).ToList();

        var entry = Assert.Single(entries);
        Assert.Equal(".NET Runtime", entry.Source);
        Assert.Contains("warning", entry.Message);
    }

    private BaGetApplication Start(Dictionary<string, string>? settings = null, CapturingLoggerProvider? logs = null)
    {
        var app = new BaGetApplication(
            _output,
            configOverrides: settings,
            configureServices: logs == null ? null : s => s.AddSingleton<ILoggerProvider>(logs));
        _disposables.Add(app);
        return app;
    }

    /// <summary>The logger as the host builds it: the code-set levels, then the given configuration.</summary>
    private static CapturingSink Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var sink = new CapturingSink();
        var loggerConfiguration = new LoggerConfiguration();

        loggerConfiguration.ApplyDefaultMinimumLevels();
        loggerConfiguration.ReadFrom.Configuration(configuration).WriteTo.Sink(sink);
        sink.Logger = loggerConfiguration.CreateLogger();
        return sink;
    }

    private static void Log(CapturingSink sink, string category, string message, LogEventLevel level = LogEventLevel.Information) =>
        sink.Logger.ForContext(Constants.SourceContextPropertyName, category).Write(level, message);

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        foreach (var directory in _directories)
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public Serilog.ILogger Logger { get; set; } = null!;

        public List<string> Messages { get; } = new();

        public void Emit(LogEvent logEvent) => Messages.Add(logEvent.RenderMessage());
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = new();

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Capture(this);

        public void Dispose() { }

        private sealed class Capture : Microsoft.Extensions.Logging.ILogger
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
