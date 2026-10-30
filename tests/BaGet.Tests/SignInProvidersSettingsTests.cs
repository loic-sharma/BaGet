using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Services.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The "Sign-in providers" panel of the Settings screen (Accounts section) and its <c>/api/ui/admin/settings</c> twin: add, edit and remove named OpenID
/// Connect providers with the password, the secret never echoed (blank keeps it), written to the overrides file, a restart needed, the read-only gate.
/// </summary>
public class SignInProvidersSettingsTests : IDisposable
{
    private const string Password = "Passw0rd!";
    private const string Key = "Accounts:OpenIdConnectProviders:";

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public SignInProvidersSettingsTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private async Task<(BaGetApplication App, Browser Admin)> StartAsync(Dictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string> { { "Accounts:Enabled", "true" } };
        foreach (var pair in extra ?? new Dictionary<string, string>()) settings[pair.Key] = pair.Value;

        var app = new BaGetApplication(_output, configOverrides: settings);
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

    private static Dictionary<string, string> Form(string id, Action<Dictionary<string, string>>? change = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["id"] = id,
            ["displayName"] = "Entra ID",
            ["authority"] = "https://login.example.com/tenant/v2.0",
            ["clientId"] = "client-1",
            ["clientSecret"] = "s3cret-value",
            ["password"] = Password,
        };
        change?.Invoke(fields);

        return fields;
    }

    private static Task<HttpResponseMessage> SaveAsync(Browser browser, Dictionary<string, string> fields)
        => browser.PostAsync("admin/settings", fields, "admin/settings?handler=SaveProvider");

    private static Task<HttpResponseMessage> RemoveAsync(Browser browser, string id, string password = Password)
        => browser.PostAsync("admin/settings", new Dictionary<string, string> { ["id"] = id, ["password"] = password }, "admin/settings?handler=RemoveProvider");

    private static IReadOnlyDictionary<string, string> Saved(BaGetApplication app)
        => app.Services.GetRequiredService<SettingsOverridesStore>().Read();

    private static Task<string> PanelAsync(Browser browser) => browser.GetPageAsync("admin/settings?section=Accounts");

    [Fact]
    public async Task TheAccountsSectionHasTheProvidersPanelWithTheAddForm()
    {
        var (_, admin) = await StartAsync();

        var html = await PanelAsync(admin);

        Assert.Contains("id=\"sign-in-providers\"", html);
        Assert.Contains("No named provider is configured.", html);
        Assert.Contains("handler=SaveProvider", html);
        Assert.Contains("name=\"administratorClaim\"", html);
        Assert.Contains("decides the administrator flag", html);
    }

    [Fact]
    public async Task AnAdministratorAddsAProviderThatIsWrittenToTheOverridesFileAndNeedsARestart()
    {
        var (app, admin) = await StartAsync();

        using (var saved = await SaveAsync(admin, Form("entra", f =>
               {
                   f["scopes"] = "groups offline_access";
                   f["parameters"] = "domain_hint=example.com\nlogin_hint=a@b.c";
                   f["userNameClaim"] = "upn";
                   f["groupClaim"] = "groups";
                   f["groupMappings"] = "dev=Developers\nops=Operators";
                   f["administratorClaim"] = "roles";
                   f["administratorValue"] = "baget-admin";
               })))
        {
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        }

        var file = Saved(app);
        Assert.Equal("https://login.example.com/tenant/v2.0", file[Key + "entra:Authority"]);
        Assert.Equal("client-1", file[Key + "entra:ClientId"]);
        Assert.Equal("s3cret-value", file[Key + "entra:ClientSecret"]);
        Assert.Equal("Entra ID", file[Key + "entra:DisplayName"]);
        Assert.Equal("groups offline_access", file[Key + "entra:Scopes"]);
        Assert.Equal("example.com", file[Key + "entra:Parameters:domain_hint"]);
        Assert.Equal("a@b.c", file[Key + "entra:Parameters:login_hint"]);
        Assert.Equal("Developers", file[Key + "entra:GroupMappings:dev"]);
        Assert.Equal("Operators", file[Key + "entra:GroupMappings:ops"]);
        Assert.Equal("roles", file[Key + "entra:AdministratorClaim"]);
        Assert.Equal("upn", file[Key + "entra:UserNameClaim"]);

        var html = await PanelAsync(admin);
        Assert.Contains("Saved the provider entra. Restart BaGet to apply it.", html);
        Assert.Contains("Restart BaGet to apply:", html);
        Assert.Contains(Key + "entra", html);
        Assert.Contains("data-provider-id=\"entra\"", html);
        Assert.Contains("Restart to apply", html);
    }

    [Fact]
    public async Task TheSecretIsNeverShownBackAndABlankOneKeepsTheSavedSecret()
    {
        var (app, admin) = await StartAsync();
        using (await SaveAsync(admin, Form("entra"))) { }

        var html = await PanelAsync(admin);
        Assert.DoesNotContain("s3cret-value", html);
        Assert.Contains("leave blank to keep it", html);

        using (await SaveAsync(admin, Form("entra", f => { f["clientSecret"] = ""; f["displayName"] = "Renamed"; }))) { }

        Assert.Equal("s3cret-value", Saved(app)[Key + "entra:ClientSecret"]);
        Assert.Equal("Renamed", Saved(app)[Key + "entra:DisplayName"]);

        using (await SaveAsync(admin, Form("entra", f => f["clientSecret"] = "n3w"))) { }
        Assert.Equal("n3w", Saved(app)[Key + "entra:ClientSecret"]);
        Assert.DoesNotContain("n3w", await PanelAsync(admin));
    }

    [Fact]
    public async Task EditingReplacesTheListsAndClearedFieldsAreRemoved()
    {
        var (app, admin) = await StartAsync();
        using (await SaveAsync(admin, Form("entra", f => { f["parameters"] = "a=1\nb=2"; f["scopes"] = "groups"; }))) { }

        using (await SaveAsync(admin, Form("entra", f => { f["parameters"] = "b=3"; f["scopes"] = ""; }))) { }

        var file = Saved(app);
        Assert.DoesNotContain(Key + "entra:Parameters:a", file.Keys);
        Assert.Equal("3", file[Key + "entra:Parameters:b"]);
        Assert.DoesNotContain(Key + "entra:Scopes", file.Keys);
    }

    [Fact]
    public async Task AWrongPasswordABadIdOrInvalidFieldsSaveNothing()
    {
        var (app, admin) = await StartAsync();

        Assert.Contains("confirmation is incorrect", await BodyAsync(await SaveAsync(admin, Form("entra", f => f["password"] = "wrong"))));
        Assert.Contains("lower-case letters", await BodyAsync(await SaveAsync(admin, Form("Not Valid"))));
        Assert.Contains("administrator claim and the administrator value go together", await BodyAsync(await SaveAsync(admin, Form("entra", f => f["administratorClaim"] = "roles"))));
        Assert.Contains("Group mappings need a group claim", await BodyAsync(await SaveAsync(admin, Form("entra", f => f["groupMappings"] = "dev=Developers"))));
        Assert.Contains("name=value", await BodyAsync(await SaveAsync(admin, Form("entra", f => f["parameters"] = "novalue"))));
        Assert.Contains("http(s) address", await BodyAsync(await SaveAsync(admin, Form("entra", f => f["authority"] = "login"))));

        Assert.DoesNotContain(Saved(app).Keys, k => k.StartsWith(Key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NamesThatDifferOnlyByCaseAreValidationErrorsNotServerErrors()
    {
        var (app, admin) = await StartAsync();

        Assert.Contains("given twice", await BodyAsync(await SaveAsync(admin, Form("entra", f => f["parameters"] = "domain_hint=a\nDomain_hint=b"))));
        Assert.Contains("given twice", await BodyAsync(await SaveAsync(admin, Form("entra", f => { f["groupClaim"] = "groups"; f["groupMappings"] = "Dev=A\ndev=B"; }))));

        Assert.DoesNotContain(Saved(app).Keys, k => k.StartsWith(Key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OidcIsRefusedAsAnIdWhileTheSingleBlockIsConfigured()
    {
        var (app, admin) = await StartAsync(new Dictionary<string, string>
        {
            { "Accounts:OpenIdConnect:Authority", "https://legacy.example.com" },
            { "Accounts:OpenIdConnect:ClientId", "legacy" },
        });

        Assert.Contains("belongs to the single", await BodyAsync(await SaveAsync(admin, Form("oidc"))));
        Assert.DoesNotContain(Saved(app).Keys, k => k.StartsWith(Key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AProviderIsRemovedWithThePasswordAndOnlyWhenTheOverridesFileHoldsIt()
    {
        var (app, admin) = await StartAsync(new Dictionary<string, string>
        {
            { Key + "fromconfig:Authority", "https://cfg.example.com" },
            { Key + "fromconfig:ClientId", "cfg" },
        });
        using (await SaveAsync(admin, Form("entra"))) { }

        Assert.Contains("confirmation is incorrect", await BodyAsync(await RemoveAsync(admin, "entra", "wrong")));
        Assert.Contains(Key + "entra:ClientId", Saved(app).Keys);

        using (var removed = await RemoveAsync(admin, "entra")) Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.DoesNotContain(Saved(app).Keys, k => k.StartsWith(Key + "entra", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Removed the provider entra", await PanelAsync(admin));

        // The one a configuration file sets is listed but cannot be changed or removed from here.
        var html = await PanelAsync(admin);
        Assert.Contains("data-provider-id=\"fromconfig\"", html);
        Assert.Contains("configuration file or environment", html);
        Assert.Contains("set by a configuration file or the environment", await BodyAsync(await RemoveAsync(admin, "fromconfig")));
        Assert.Contains("set by a configuration file or the environment", await BodyAsync(await SaveAsync(admin, Form("fromconfig"))));
        Assert.Contains("There is no saved provider", await BodyAsync(await RemoveAsync(admin, "ghost")));
    }

    [Fact]
    public async Task OnlyAnAdministratorReachesTheHandlers()
    {
        var (app, _) = await StartAsync();
        using var alice = new Browser(app);
        await alice.RegisterAsync("alice");

        using var save = await alice.PostAsync("account/settings", Form("entra"), "admin/settings?handler=SaveProvider");
        using var remove = await alice.PostAsync("account/settings", new Dictionary<string, string> { ["id"] = "entra", ["password"] = Password }, "admin/settings?handler=RemoveProvider");

        Assert.Equal(HttpStatusCode.NotFound, save.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.DoesNotContain(Saved(app).Keys, k => k.StartsWith(Key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WhileTheDatabaseIsReadOnlyTheProviderChangesAreRefusedWith503()
    {
        var (app, admin) = await StartAsync();
        using (await SaveAsync(admin, Form("entra"))) { }
        app.Services.GetRequiredService<MaintenanceGate>().Close("maintenance");

        using var save = await SaveAsync(admin, Form("google"));
        using var remove = await RemoveAsync(admin, "entra");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, save.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, remove.StatusCode);
        Assert.Contains(Key + "entra:ClientId", Saved(app).Keys);
        Assert.DoesNotContain(Key + "google:ClientId", Saved(app).Keys);
    }

    [Fact]
    public async Task TheLegacyBlockStaysAsItWasInTheSettingsRows()
    {
        var (_, admin) = await StartAsync();

        var html = await PanelAsync(admin);

        Assert.Contains("Accounts:OpenIdConnect:Authority", html);
        Assert.Contains("Accounts:OpenIdConnect:ClientSecret", html);
        Assert.DoesNotContain("Accounts:OpenIdConnectProviders\"", html); // the dictionary is no row
    }

    // ----- the JSON twin

    private async Task<(JsonNode? Json, HttpStatusCode Status)> TwinAsync(BaGetApplication app, string key, string path, object? body)
    {
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, path);
        if (body != null)
        {
            request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return (text.Length > 0 && text[0] == '{' ? JsonNode.Parse(text) : null, response.StatusCode);
    }

    private static async Task<string> AdminKeyAsync(BaGetApplication app)
    {
        using var scope = app.Services.CreateScope();
        var root = await AdminTestSupport.FindAsync(app, "root");

        return await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync(root!.Id, "twin", new[] { ApiKeyScopes.Admin }, "*", null, null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheTwinSavesAndRemovesAProviderWithThePasswordInTheBodyAndNeverReturnsTheSecret()
    {
        var (app, _) = await StartAsync();
        var key = await AdminKeyAsync(app);

        var bad = await TwinAsync(app, key, "api/ui/admin/settings/save-provider", new { id = "entra", authority = "https://login.example.com", clientId = "c", clientSecret = "twin-secret", password = "wrong" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);

        var saved = await TwinAsync(app, key, "api/ui/admin/settings/save-provider", new { id = "entra", displayName = "Entra", authority = "https://login.example.com", clientId = "c", clientSecret = "twin-secret", password = Password });
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal("Next", saved.Json!["status"]!.GetValue<string>());
        Assert.DoesNotContain("twin-secret", saved.Json.ToJsonString());
        var listed = await TwinAsync(app, key, "api/ui/admin/settings", null);
        var provider = listed.Json!["model"]!["signInProviders"]!.AsArray().Single()!;
        Assert.Equal("entra", provider["id"]!.GetValue<string>());
        Assert.True(provider["hasSecret"]!.GetValue<bool>());
        Assert.True(provider["pendingRestart"]!.GetValue<bool>());
        Assert.DoesNotContain("twin-secret", listed.Json.ToJsonString());
        Assert.Equal("twin-secret", Saved(app)[Key + "entra:ClientSecret"]);

        var removed = await TwinAsync(app, key, "api/ui/admin/settings/remove-provider", new { id = "entra", password = Password });
        Assert.Equal(HttpStatusCode.OK, removed.Status);
        Assert.Empty((await TwinAsync(app, key, "api/ui/admin/settings", null)).Json!["model"]!["signInProviders"]!.AsArray());
        Assert.DoesNotContain(Saved(app).Keys, k => k.StartsWith(Key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheTwinRefusesWithoutAnAdministratorKey()
    {
        var (app, _) = await StartAsync();

        var anonymous = await TwinAsync(app, "nonsense", "api/ui/admin/settings/save-provider", new { id = "entra", password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.Status);
    }
}
