using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The per-source log levels (<c>Serilog:MinimumLevel:Override</c>) of the Settings page's Serilog section: administrators
/// only, password-confirmed, refused while the maintenance gate is closed, saved to the overrides file.
/// </summary>
public class LogLevelPageTests : IDisposable
{
    private const string Password = "Passw0rd!";
    private const string Key = "Serilog:MinimumLevel:Override:";

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public LogLevelPageTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private async Task<(BaGetApplication App, Browser Admin)> StartAsync()
    {
        var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" } });
        _disposables.Add(app);
        var admin = new Browser(app);
        _disposables.Add(admin);

        await admin.RegisterAsync("root");
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AdministratorService>()
                .PromoteAsync("root", "test", TestContext.Current.CancellationToken);
        }

        return (app, admin);
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response)
    {
        using (response) return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> SetAsync(Browser browser, string source, string level, string password = Password, string tokenPage = "admin/settings")
        => browser.PostAsync("admin/settings", new Dictionary<string, string> { ["source"] = source, ["level"] = level, ["password"] = password }, "admin/settings?handler=SetLogLevel", tokenPage);

    private static Task<HttpResponseMessage> RemoveAsync(Browser browser, string source, string password = Password)
        => browser.PostAsync("admin/settings", new Dictionary<string, string> { ["source"] = source, ["password"] = password }, "admin/settings?handler=RemoveLogLevel");

    private static IReadOnlyDictionary<string, string> Saved(BaGetApplication app)
        => app.Services.GetRequiredService<SettingsOverridesStore>().Read();

    [Fact]
    public async Task TheSerilogSectionListsTheConfiguredLevelsAndOffersTheForm()
    {
        var (_, admin) = await StartAsync();

        var html = await admin.GetPageAsync("admin/settings?section=Serilog");

        Assert.Contains("id=\"log-level-form\"", html);
        Assert.Contains("id=\"log-levels-table\"", html); // appsettings.json already sets Microsoft and the request diagnostics
        Assert.Contains("configuration file or environment", html);
        Assert.Contains("<option value=\"Warning\">", html);
    }

    [Fact]
    public async Task AnAdministratorSetsAndRemovesALevelAndTheFileFollows()
    {
        var (app, admin) = await StartAsync();

        using (var saved = await SetAsync(admin, "Microsoft.AspNetCore", "warning")) Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var html = await admin.GetPageAsync("admin/settings?section=Serilog");
        Assert.Contains("The level of Microsoft.AspNetCore was set to Warning.", html);
        Assert.Equal("Warning", Saved(app)[Key + "Microsoft.AspNetCore"]);
        Assert.Contains("data-log-source=\"Microsoft.AspNetCore\"", html);
        Assert.Contains("log-level-remove", html);

        using (var removed = await RemoveAsync(admin, "Microsoft.AspNetCore")) Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);

        Assert.Contains("The level of Microsoft.AspNetCore was removed.", await admin.GetPageAsync("admin/settings?section=Serilog"));
        Assert.DoesNotContain(Key + "Microsoft.AspNetCore", Saved(app).Keys);
    }

    [Fact]
    public async Task AWrongPasswordABadSourceOrABadLevelChangesNothing()
    {
        var (app, admin) = await StartAsync();

        Assert.Contains("confirmation is incorrect", await BodyAsync(await SetAsync(admin, "System", "Error", password: "wrong")));
        Assert.Contains("not a level", await BodyAsync(await SetAsync(admin, "System", "Loud")));
        Assert.Contains("letters, digits", await BodyAsync(await SetAsync(admin, "Bad:Name", "Error")));

        Assert.DoesNotContain(Saved(app).Keys, k => k.StartsWith(Key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OnlyAnAdministratorReachesTheHandlers()
    {
        var (app, _) = await StartAsync();
        using var alice = new Browser(app);
        await alice.RegisterAsync("alice");

        using var response = await SetAsync(alice, "System", "Error", tokenPage: "account/settings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(Saved(app).Keys, k => k.StartsWith(Key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WhileTheDatabaseIsReadOnlyTheLevelIsRefusedWith503()
    {
        var (app, admin) = await StartAsync();
        app.Services.GetRequiredService<MaintenanceGate>().Close("maintenance");

        using var response = await SetAsync(admin, "System", "Error");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(Saved(app).Keys, k => k.StartsWith(Key, StringComparison.OrdinalIgnoreCase));
    }
}
