using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Npgsql;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The drop-downs and connection string examples of the Settings page, and the startup
/// validation that shares their lists.
/// </summary>
public class SettingsChoicePageTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public SettingsChoicePageTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private async Task<string> SettingsPageAsync()
    {
        var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" } });
        _disposables.Add(app);
        var admin = new Browser(app);
        _disposables.Add(admin);

        await admin.RegisterAsync("root");
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AdministratorService>()
                .PromoteAsync("root", "test", System.Threading.CancellationToken.None);
        }

        return await admin.GetPageAsync("admin/settings");
    }

    private static string Tag(string html, string idAttribute)
        => Regex.Match(html, "<[^>]*" + Regex.Escape(idAttribute) + "[^>]*>").Value;

    [Fact]
    public async Task DatabaseTypeIsADropDownOfTheFiveTypesWithTheCurrentOneSelected()
    {
        var html = await SettingsPageAsync();

        var select = Regex.Match(html, "<select id=\"Database_Type\".*?</select>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(select);
        Assert.Contains("name=\"v:Database:Type\"", select);
        foreach (var type in new[] { "AzureTable", "MySql", "PostgreSql", "Sqlite", "SqlServer" })
        {
            Assert.Contains($"<option value=\"{type}\"", select);
        }

        Assert.Equal(5, Regex.Matches(select, "<option ").Count);
        Assert.Contains("<option value=\"Sqlite\" selected=\"selected\">", select);
        Assert.Single(Regex.Matches(select, " selected="));
    }

    [Fact]
    public async Task StorageTypeSelectsTheCurrentValueIgnoringCase()
    {
        // The test host sets "FileSystem"; the list says "Filesystem".
        var html = await SettingsPageAsync();

        var select = Regex.Match(html, "<select id=\"Storage_Type\".*?</select>", RegexOptions.Singleline).Value;
        Assert.Contains("<option value=\"Filesystem\" selected=\"selected\">", select);
        Assert.DoesNotContain("not in the list", select);
        Assert.Equal(6, Regex.Matches(select, "<option ").Count);
    }

    [Fact]
    public async Task TheConnectionStringInputNeverCarriesAnExampleAsItsValue()
    {
        var html = await SettingsPageAsync();

        var input = Tag(html, "id=\"Database_ConnectionString\"");
        Assert.Contains("type=\"password\"", input);
        Assert.Contains("value=\"\"", input);
        Assert.Contains("unchanged", input); // a value is stored, so the unchanged placeholder stays
        Assert.DoesNotContain("Data Source=baget.db", input);
    }

    [Fact]
    public async Task TheHelpLineCarriesEveryExampleAsDataAttributes()
    {
        var html = await SettingsPageAsync();

        var line = Tag(html, "id=\"Database_ConnectionString_example\"");
        Assert.Contains("data-example-source=\"Database_Type\"", line);
        Assert.Contains("data-example-target=\"Database_ConnectionString\"", line);
        Assert.Contains("data-example-stored=\"true\"", line);
        var json = System.Net.WebUtility.HtmlDecode(
            System.Text.RegularExpressions.Regex.Match(line, "data-examples=\"([^\"]*)\"").Groups[1].Value);
        var examples = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("Data Source=baget.db", examples["sqlite"]);
        Assert.Equal("Server=localhost;Database=BaGet;Trusted_Connection=True;TrustServerCertificate=True", examples["sqlserver"]);
        Assert.Equal("Host=localhost;Port=5432;Database=baget;Username=baget;Password=<password>", examples["postgresql"]);
        Assert.Equal("Server=localhost;Port=3306;Database=baget;User=baget;Password=<password>", examples["mysql"]);
        Assert.Equal("DefaultEndpointsProtocol=https;AccountName=<account>;AccountKey=<key>;EndpointSuffix=core.windows.net", examples["azuretable"]);
        Assert.DoesNotContain("hidden", line); // the test host uses Sqlite, which has an example
    }

    [Fact]
    public async Task TheHelpLineShowsTheExampleForTheCurrentTypeEncoded()
    {
        var html = await SettingsPageAsync();

        Assert.Contains(
            "Example for <span data-example-type=\"\">Sqlite</span>: <code data-example-text=\"\">Data Source=baget.db</code>",
            html);
        Assert.DoesNotContain("<password>", html);
    }

    [Fact]
    public async Task ThePageScriptWritesOnlyTextAndAttributes()
    {
        var html = await SettingsPageAsync();

        var script = html.Substring(html.IndexOf("function parse", StringComparison.Ordinal));
        script = script.Substring(0, script.IndexOf("</script>", StringComparison.Ordinal));
        Assert.Contains("textContent", script);
        Assert.Contains("setAttribute('placeholder'", script);
        Assert.DoesNotContain("innerHTML", script);
        Assert.DoesNotContain("insertAdjacentHTML", script);
        Assert.DoesNotContain(".value =", script);
    }

    // --- startup validation shares the lists ---

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string database, string storage, string search)
    {
        var options = new BaGetOptions
        {
            Database = new DatabaseOptions { Type = database },
            Storage = new StorageOptions { Type = storage },
            Search = new SearchOptions { Type = search },
            Mirror = new MirrorOptions(),
        };

        return new ConfigureBaGetOptions(new ConfigurationBuilder().Build()).Validate(null, options);
    }

    [Fact]
    public void StartupValidationAcceptsEveryValueTheAttributesListInAnyCase()
    {
        foreach (var database in AllowedSettingValuesAttribute.ValuesOf<DatabaseOptions>(nameof(DatabaseOptions.Type)))
        foreach (var storage in AllowedSettingValuesAttribute.ValuesOf<StorageOptions>(nameof(StorageOptions.Type)))
        foreach (var search in AllowedSettingValuesAttribute.ValuesOf<SearchOptions>(nameof(SearchOptions.Type)))
        {
            Assert.True(Validate(database, storage, search).Succeeded, $"{database}/{storage}/{search}");
            Assert.True(Validate(database.ToUpperInvariant(), storage.ToLowerInvariant(), search.ToUpperInvariant()).Succeeded);
        }
    }

    [Fact]
    public void StartupValidationRefusesOtherValuesAndNamesTheAttributeValues()
    {
        var result = Validate("Oracle", "Nowhere", "Nothing");

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
        Assert.Contains(
            $"The 'Database:Type' config is invalid. Allowed values: {string.Join(", ", AllowedSettingValuesAttribute.ValuesOf<DatabaseOptions>("Type"))}",
            result.Failures!);
        Assert.Contains(
            $"The 'Storage:Type' config is invalid. Allowed values: {string.Join(", ", AllowedSettingValuesAttribute.ValuesOf<StorageOptions>("Type"))}",
            result.Failures!);
        Assert.Contains(
            $"The 'Search:Type' config is invalid. Allowed values: {string.Join(", ", AllowedSettingValuesAttribute.ValuesOf<SearchOptions>("Type"))}",
            result.Failures!);
    }

    // --- the examples are valid for their provider ---

    private static string Example(string type)
        => new SettingsCatalog(new[] { new SettingsSchemaRegistration(typeof(DatabaseOptions), "Database") })
            .Find("Database:ConnectionString")!.Examples![type];

    [Fact]
    public void TheSqliteExampleParses()
    {
        var builder = new SqliteConnectionStringBuilder(Example("Sqlite"));

        Assert.Equal("baget.db", builder.DataSource);
    }

    [Fact]
    public void TheSqlServerExampleParses()
    {
        var builder = new SqlConnectionStringBuilder(Example("SqlServer"));

        Assert.Equal("localhost", builder.DataSource);
        Assert.Equal("BaGet", builder.InitialCatalog);
        Assert.True(builder.IntegratedSecurity);
        Assert.True(builder.TrustServerCertificate);
    }

    [Fact]
    public void ThePostgreSqlExampleParses()
    {
        var builder = new NpgsqlConnectionStringBuilder(Example("PostgreSql"));

        Assert.Equal("localhost", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("baget", builder.Database);
        Assert.Equal("baget", builder.Username);
        Assert.Equal("<password>", builder.Password);
    }

    [Fact]
    public void TheMySqlExampleParses()
    {
        var builder = new MySqlConnectionStringBuilder(Example("MySql"));

        Assert.Equal("localhost", builder.Server);
        Assert.Equal(3306u, builder.Port);
        Assert.Equal("baget", builder.Database);
        Assert.Equal("baget", builder.UserID);
        Assert.Equal("<password>", builder.Password);
    }

    [Fact]
    public void TheAzureTableExampleParsesOnceItsPlaceholdersAreFilled()
    {
        var filled = Example("AzureTable").Replace("<account>", "devaccount").Replace("<key>", Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }));

        var account = CloudStorageAccount.Parse(filled);

        Assert.Equal("devaccount", account.Credentials.AccountName);
        Assert.StartsWith("https://devaccount.table.core.windows.net", account.TableEndpoint.ToString());
    }
}
