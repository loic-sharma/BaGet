using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>A second format for the integration tests: an npm-like module with create fields (it replaces the real npm module).</summary>
internal sealed class FakeNpmModule : IFormatModule
{
    public RepositoryFormat Format => RepositoryFormat.Npm;
    public string DisplayName => "npm";
    public string RoutePrefix => "npm";
    public string FeedPath => "npm/";
    public IReadOnlyCollection<RepositoryKind> CreatableKinds { get; } = new[] { RepositoryKind.Local };
    public IReadOnlyList<string> StorageFolders { get; } = new[] { "npm" };

    public IReadOnlyList<FormatCreateField> CreateFields { get; } = new[]
    {
        new FormatCreateField("scope", "Default scope", "Starts with @.", Required: false),
        new FormatCreateField("access", "Access", null, false, new[] { "public", "restricted" }),
    };

    public string? CreateSettings(IReadOnlyDictionary<string, string?> fields)
    {
        fields.TryGetValue("scope", out var scope);
        if (!string.IsNullOrEmpty(scope) && !scope.StartsWith('@')) throw new ArgumentException("A scope starts with @.");

        return "{\"scope\":\"" + scope + "\",\"access\":\"" + (fields.TryGetValue("access", out var a) ? a : "") + "\"}";
    }

    public IReadOnlyList<FormatLink> RepositoryLinks(RepositoryInfo repository)
        => new[] { FormatLink.ToPath("registry", "Registry", "npm/") };

    public async Task<IReadOnlyDictionary<int, int>> CountArtifactsAsync(IContext context, CancellationToken cancellationToken)
        => await context.NpmPackages!.IgnoreQueryFilters().GroupBy(p => p.RepositoryKey)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    public async Task<FormatUsage> GetUsageAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
        => new(await context.NpmPackages!.IgnoreQueryFilters().CountAsync(p => p.RepositoryKey == repositoryKey, cancellationToken), false);

    public async Task StagePurgeAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
        => context.NpmPackages!.RemoveRange(await context.NpmPackages!.IgnoreQueryFilters().Where(p => p.RepositoryKey == repositoryKey).ToListAsync(cancellationToken));
}

/// <summary>
/// The format layer in the administrator's pages: the Package formats list, a format's create form, its links and feed address,
/// and a NuGet route answering 404 in a repository of another format.
/// </summary>
public class AdminFormatsTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _admin;

    public AdminFormatsTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } },
            configureServices: services =>
            {
                // The real npm module is replaced by one with create fields, to drive the generic create form of the format layer.
                services.RemoveAll<NpmFormatModule>();
                foreach (var real in services.Where(d => d.ServiceType == typeof(IFormatModule) && d.ImplementationType == typeof(NpmFormatModule)).ToList())
                {
                    services.Remove(real);
                }

                services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormatModule, FakeNpmModule>());
            });
        _admin = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
    }

    private Task<HttpResponseMessage> CreateNpmAsync(string name, string scope = "@acme", string access = "public", string? format = "Npm")
    {
        var fields = new Dictionary<string, string>
        {
            { "format", format ?? "" }, { "name", name }, { "field:scope", scope }, { "field:access", access }, { "anonymousRead", "true" },
        };

        return _admin.PostAsync("admin/repositories", fields, action: "admin/repositories?handler=Format", tokenPage: "admin/repositories");
    }

    private async Task<Repository?> RowAsync(string name)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IContext>().Repositories!.AsNoTracking().FirstOrDefaultAsync(r => r.Name == name);
    }

    [Fact]
    public async Task OnlyAdministratorsSeeThePackageFormatsPage()
    {
        await CreateUserAsync(_app, "alice");
        using var alice = new Browser(_app);
        await SignInAsync(alice, "alice");
        using var anonymous = new Browser(_app);

        using var asUser = await alice.GetAsync("admin/formats");
        using var asAnonymous = await anonymous.GetAsync("admin/formats");

        Assert.Equal(HttpStatusCode.NotFound, asUser.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, asAnonymous.StatusCode);
    }

    [Fact]
    public async Task ThePageListsEveryRegisteredFormatWithItsFeedFoldersAndRepositoryCount()
    {
        var html = await _admin.GetPageAsync("admin/formats");

        Assert.Contains("data-format=\"NuGet\"", html);
        Assert.Contains("v3/index.json", html);
        Assert.Contains("packages, symbols, catalog, validation, staging", html);
        Assert.Contains("Deploy (indexing), Search", html);
        Assert.Contains("data-format=\"Npm\"", html);
        Assert.Contains("npm/", html);
        Assert.Contains("/admin/repositories", html);
    }

    [Fact]
    public async Task TheMenuAndTheRepositoriesPageLinkToTheFormatsPage()
    {
        Assert.Contains("href=\"/admin/formats\"", await _admin.GetPageAsync("admin/repositories"));
        Assert.Contains("id=\"formats-link\"", await _admin.GetPageAsync("admin/repositories"));
    }

    [Fact]
    public async Task AFormatsCreateFormListsItsFieldsAndNuGetsFormsStayAsTheyWere()
    {
        var html = await _admin.GetPageAsync("admin/repositories");

        Assert.Contains("id=\"format-create-form-npm\"", html);
        Assert.Contains("name=\"field:scope\"", html);
        Assert.Contains("<option value=\"restricted\">", html);
        Assert.Contains("id=\"repository-create-form\"", html);
        Assert.Contains("id=\"remote-create-form\"", html);
        Assert.Contains("id=\"virtual-create-form\"", html);
        Assert.DoesNotContain("format-create-form-nuget", html);
    }

    [Fact]
    public async Task CreatingARepositoryOfAnotherFormatStoresTheModulesSettingsAndShowsItsFeedAndLinks()
    {
        using var response = await CreateNpmAsync("webapp", access: "restricted");
        Assert.True(response.IsSuccessStatusCode || (int)response.StatusCode == 302 || (int)response.StatusCode == 200, response.StatusCode.ToString());

        var row = await RowAsync("webapp");
        Assert.NotNull(row);
        Assert.Equal(RepositoryFormat.Npm, row!.Format);
        Assert.Equal("{\"scope\":\"@acme\",\"access\":\"restricted\"}", row.Settings);

        var html = await _admin.GetPageAsync("admin/repositories");
        Assert.Contains("data-repository=\"webapp\"", html);
        Assert.Contains("/r/webapp/npm/", html);
        Assert.Contains("class=\"repository-registry\"", html);
        // NuGet's links belong to NuGet repositories only.
        var npmRow = html.Substring(html.IndexOf("data-repository=\"webapp\"", StringComparison.Ordinal));
        npmRow = npmRow.Substring(0, npmRow.IndexOf("</tr>", StringComparison.Ordinal));
        Assert.DoesNotContain("repository-browse", npmRow);
        Assert.DoesNotContain("repository-upload", npmRow);
        Assert.DoesNotContain("v3/index.json", npmRow);
    }

    [Fact]
    public async Task ANuGetRepositoryKeepsItsBrowseAndUploadLinksAndItsFeed()
    {
        var html = await _admin.GetPageAsync("admin/repositories");
        var row = html.Substring(html.IndexOf("data-repository=\"default\"", StringComparison.Ordinal));
        row = row.Substring(0, row.IndexOf("</tr>", StringComparison.Ordinal));

        Assert.Contains("class=\"repository-browse\"", row);
        Assert.Contains("class=\"repository-upload\"", row);
        Assert.Contains("/v3/index.json", row);
    }

    [Fact]
    public async Task AnInvalidFieldIsShownAndNothingIsCreated()
    {
        using var response = await CreateNpmAsync("webapp", scope: "acme");

        Assert.Contains("A scope starts with @.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Null(await RowAsync("webapp"));
    }

    [Fact]
    public async Task AFormatWithoutAModuleIsNotFoundAndNuGetHasNoGenericForm()
    {
        // Every format of RepositoryFormat has a module now; a name that is no format at all is refused the same way.
        using var docker = await CreateNpmAsync("jars", format: "Helm");
        using var nuget = await CreateNpmAsync("feed2", format: "NuGet");
        using var empty = await CreateNpmAsync("feed3", format: null);

        Assert.Equal(HttpStatusCode.NotFound, docker.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nuget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
        Assert.Null(await RowAsync("jars"));
        Assert.Null(await RowAsync("feed2"));
    }

    [Fact]
    public async Task CreatingIsRefusedForNonAdministratorsAndWhileTheGateIsClosed()
    {
        await CreateUserAsync(_app, "alice");
        using var alice = new Browser(_app);
        await SignInAsync(alice, "alice");
        using var asUser = await alice.RawPostAsync("admin/repositories?handler=Format", new Dictionary<string, string> { { "format", "Npm" }, { "name", "byuser" } });
        Assert.NotEqual(HttpStatusCode.OK, asUser.StatusCode);
        Assert.Null(await RowAsync("byuser"));

        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("Copying the database");
        try
        {
            using var closed = await CreateNpmAsync("gated");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, closed.StatusCode);
        }
        finally
        {
            gate.Open();
        }

        Assert.Null(await RowAsync("gated"));
    }

    [Fact]
    public async Task ANuGetRouteInARepositoryOfAnotherFormatIs404AndNuGetRepositoriesStillWork()
    {
        (await CreateNpmAsync("webapp")).Dispose();

        using var inNpm = await _admin.GetAsync("r/webapp/v3/index.json");
        using var inDefault = await _admin.GetAsync("v3/index.json");

        Assert.Equal(HttpStatusCode.NotFound, inNpm.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inDefault.StatusCode);
    }

    [Fact]
    public async Task AnEmptyRepositoryOfAnotherFormatCanBeDeletedAndOneWithPackagesCannot()
    {
        (await CreateNpmAsync("webapp")).Dispose();
        (await CreateNpmAsync("busy")).Dispose();
        using (var scope = _app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            var busy = await context.Repositories!.SingleAsync(r => r.Name == "busy", TestContext.Current.CancellationToken);
            context.NpmPackages!.Add(new NpmPackage { RepositoryKey = busy.Key, Name = "left-pad", Created = DateTime.UtcNow, Modified = DateTime.UtcNow });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var empty = await _admin.PostAsync("admin/repositories/webapp/delete", new Dictionary<string, string>(), tokenPage: "admin/repositories");
        using var full = await _admin.PostAsync("admin/repositories/busy/delete", new Dictionary<string, string>(), tokenPage: "admin/repositories");

        Assert.Null(await RowAsync("webapp"));
        Assert.NotNull(await RowAsync("busy"));
    }
}
