using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The account links in the site header, and the upgrade guidance for accounts being on.
/// </summary>
public class HeaderLinksTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public HeaderLinksTests(ITestOutputHelper output) => _output = output;

    private (BaGetApplication App, Browser Browser) Start(
        bool accounts = true,
        bool allowRegistration = true,
        string apiKey = "",
        Action<IServiceCollection>? configureServices = null)
    {
        var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", accounts ? "true" : "false" },
            { "Accounts:AllowRegistration", allowRegistration ? "true" : "false" },
            { "ApiKey", apiKey },
        }, configureServices: configureServices);
        var browser = new Browser(app);
        _disposables.Add(app);
        _disposables.Add(browser);

        return (app, browser);
    }

    [Fact]
    public async Task SignedOutShowsSignInAndRegister()
    {
        var (_, browser) = Start();

        var html = await browser.GetPageAsync("stats");

        Assert.Contains("account/login", html);
        Assert.Contains("Sign in", html);
        Assert.Contains("account/register", html);
        Assert.Contains("Register", html);
        Assert.DoesNotContain("account/logout", html);
    }

    [Fact]
    public async Task RegisterIsHiddenWhenRegistrationIsClosed()
    {
        var (_, browser) = Start(allowRegistration: false);

        var html = await browser.GetPageAsync("stats");

        Assert.Contains("account/login", html);
        Assert.DoesNotContain("account/register", html);
    }

    [Fact]
    public async Task SignedInShowsTheAccountMenu()
    {
        var (_, browser) = Start();
        await browser.RegisterAsync("alice");

        var html = await browser.GetPageAsync("stats");

        Assert.Contains("alice", html);
        Assert.Contains("account/apikeys", html);
        Assert.Contains("account/packages", html);
        Assert.Contains("account/organizations", html);
        Assert.Contains("/upload", html);
        Assert.Contains("account/logout", html);
        Assert.Contains("dropdown", html);
        Assert.DoesNotContain("account/login", html);
        Assert.DoesNotContain("account/register", html);

        Assert.Contains("account/settings", html);
    }

    [Fact]
    public async Task DisabledAccountsLeaveNoAccountLinks()
    {
        var (_, browser) = Start(accounts: false);

        var html = await browser.GetPageAsync("stats");

        Assert.DoesNotContain("account/", html);
    }

    [Fact]
    public async Task UploadPageSaysAKeyIsRequiredAndLinksToTheKeysPage()
    {
        var (_, browser) = Start();

        var html = await browser.GetPageAsync("upload");

        Assert.Contains("-k &lt;your API key&gt;", html);
        Assert.Contains("href=\"/account/apikeys\"", html);
    }

    [Fact]
    public async Task UploadPageHasNoKeyNoteWithoutAccounts()
    {
        var (_, browser) = Start(accounts: false, apiKey: "k");

        var html = await browser.GetPageAsync("upload");

        Assert.DoesNotContain("your API key", html);
    }

    [Fact]
    public async Task StartupWarnsThatKeylessPushesAreRefused()
    {
        var logs = new CapturingLoggerProvider();
        var (app, _) = Start(configureServices: s => s.AddSingleton<ILoggerProvider>(logs));

        _ = app.CreateClient();

        Assert.Contains(logs.Messages, m => m.Contains("keyless", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StartupDoesNotWarnWhenAGlobalKeyIsSet()
    {
        var logs = new CapturingLoggerProvider();
        var (app, _) = Start(apiKey: "k", configureServices: s => s.AddSingleton<ILoggerProvider>(logs));

        _ = app.CreateClient();

        Assert.DoesNotContain(logs.Messages, m => m.Contains("keyless", StringComparison.OrdinalIgnoreCase));
        await Task.CompletedTask;
    }

    [Fact]
    public void TheShippedConfigurationEnablesAccounts()
    {
        var (app, _) = Start();
        var root = app.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;
        var path = Path.Combine(root, "appsettings.json");

        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var accounts = document.RootElement.GetProperty("Accounts");

        Assert.True(accounts.GetProperty("Enabled").GetBoolean());
        Assert.True(accounts.GetProperty("AllowRegistration").GetBoolean());
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables.AsEnumerable().Reverse())
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
