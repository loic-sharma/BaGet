using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// The administrator's pages for remote and virtual npm repositories: the same forms as NuGet's, format-aware. Create from the
/// Repositories page (the form asks for the format), edit the upstream, test the connection, look at and purge the cache, add
/// members of the right format and ask where a package version would come from.
/// </summary>
public class AdminNpmRemoteVirtualTests : IDisposable
{
    private const string Url = "http://registry.test/";
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly FakeNpmUpstream _upstream = new();
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly BaGetApplication _app;
    private readonly Browser _admin;

    public AdminNpmRemoteVirtualTests(ITestOutputHelper output)
    {
        _wire = new ForwardingRemoteHttpClientFactory(new HttpClient(_upstream));
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } },
            configureServices: services => services.AddSingleton<IRemoteHttpClientFactory>(_wire));
        _admin = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
        _app.CreateRepositoryAsync("nuget-one", Cancel).GetAwaiter().GetResult();
        _app.CreateNpmRepositoryAsync("loc", cancellationToken: Cancel).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
    }

    private static Dictionary<string, string> RemoteForm(string name, string format = "Npm", Action<Dictionary<string, string>>? change = null)
    {
        var fields = new Dictionary<string, string>
        {
            { "name", name },
            { "format", format },
            { "password", Password },
            { "Remote.Url", Url },
            { "Remote.Protocol", "V3" },
            { "Remote.Username", "" },
            { "Remote.Password", "" },
            { "Remote.MetadataTtlSeconds", "600" },
            { "Remote.NegativeTtlSeconds", "60" },
            { "Remote.DownloadTimeoutSeconds", "600" },
            { "Remote.StoreLocally", "true" },
            { "Remote.Offline", "false" },
        };
        change?.Invoke(fields);

        return fields;
    }

    private Task<HttpResponseMessage> CreateRemoteAsync(string name = "up", string format = "Npm", Action<Dictionary<string, string>>? change = null)
        => _admin.PostAsync("admin/repositories", RemoteForm(name, format, change), action: "admin/repositories?handler=Remote");

    private Task<HttpResponseMessage> CreateVirtualAsync(string name, string? format = "Npm")
    {
        var fields = new Dictionary<string, string> { { "name", name } };
        if (format != null) fields["format"] = format;

        return _admin.PostAsync("admin/repositories", fields, action: "admin/repositories?handler=Virtual");
    }

    private async Task<RepositoryInfo?> FindAsync(string name)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(name, Cancel);
    }

    private async Task<string> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            return await response.Content.ReadAsStringAsync(Cancel);
        }
    }

    private async Task<string> PostThenReadAsync(string page, string handler, Dictionary<string, string> fields)
    {
        using (var response = await _admin.PostAsync(page, fields, action: $"{page}?handler={handler}"))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        return await _admin.GetPageAsync(page);
    }

    // ---- the create forms ----

    [Fact]
    public async Task TheCreateFormsOfRemoteAndVirtualRepositoriesAskForTheFormat()
    {
        var page = await _admin.GetPageAsync("admin/repositories");

        Assert.Contains("id=\"remote-format\"", page);
        Assert.Contains("id=\"virtual-format\"", page);
        Assert.Contains("<option value=\"Npm\"", page);
        Assert.Contains("<option value=\"NuGet\"", page);
        Assert.Contains("registry.npmjs.org", page);
    }

    [Fact]
    public async Task ARemoteNpmRepositoryIsCreatedFromTheFormListedAndOpened()
    {
        using var created = await CreateRemoteAsync(change: f =>
        {
            f["Remote.Username"] = "";
            f["Remote.Password"] = "s3cret-token";
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.EndsWith("/admin/repositories/up", created.Headers.Location!.OriginalString);

        var info = await FindAsync("up");
        Assert.Equal(RepositoryFormat.Npm, info!.Format);
        Assert.Equal(RepositoryKind.Remote, info.Kind);
        using (var scope = _app.Services.CreateScope())
        {
            var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync("up", Cancel);
            Assert.Equal(RemoteProtocol.Npm, detail!.Settings.Protocol);
            Assert.True(detail.HasPassword);
        }

        var list = await _admin.GetPageAsync("admin/repositories");
        Assert.Contains("Remote npm repository up was created.", list);
        Assert.Contains("Npm / Remote", list);
        Assert.Contains("/r/up/npm/", list);

        var page = await _admin.GetPageAsync("admin/repositories/up");
        Assert.Contains("id=\"remote-protocol-npm\"", page);
        Assert.DoesNotMatch("<select[^>]*Remote_Protocol", page);
        Assert.Contains("https://registry.npmjs.org/", page);
        Assert.Contains("A password is stored", page);
        Assert.DoesNotContain("s3cret-token", page);
        Assert.Contains("id=\"remote-test\"", page);
        Assert.Contains("id=\"remote-purge-form\"", page);
        Assert.Contains("bearer token", page);
        Assert.Contains("a remote repository takes no deployments", page);
        Assert.Contains("href=\"/r/up/npm-packages\"", page);
    }

    [Fact]
    public async Task AnUnknownFormatOrOneWithoutTheKindIsNotFound()
    {
        using var unknown = await CreateRemoteAsync(format: "Oci");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var unknownVirtual = await CreateVirtualAsync("v", "Oci");
        Assert.Equal(HttpStatusCode.NotFound, unknownVirtual.StatusCode);
        Assert.Null(await FindAsync("v"));
    }

    [Fact]
    public async Task ANuGetRemoteIsStillCreatedWhenTheFormSendsNoFormat()
    {
        var fields = RemoteForm("nuget-up");
        fields.Remove("format");
        fields["Remote.Url"] = "http://localhost/v3/index.json";

        using var created = await _admin.PostAsync("admin/repositories", fields, action: "admin/repositories?handler=Remote");

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal(RepositoryFormat.NuGet, (await FindAsync("nuget-up"))!.Format);
    }

    [Fact]
    public async Task InvalidNpmUpstreamSettingsAreRefusedWithAMessage()
    {
        var html = await ReadAsync(await CreateRemoteAsync(change: f => f["Remote.Url"] = "http://u:p@host/"));

        Assert.Contains("id=\"repositories-error\"", html);
        Assert.Contains("not in the URL", html);
        Assert.Null(await FindAsync("up"));
    }

    // ---- editing a remote npm repository ----

    [Fact]
    public async Task SavingKeepsTheNpmProtocolAndTheStoredPassword()
    {
        (await CreateRemoteAsync(change: f => f["Remote.Password"] = "tok")).Dispose();

        using var saved = await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "anonymousRead", "true" },
            { "password", Password },
            { "Remote.Url", "http://registry.test/other/" },
            { "Remote.Protocol", "V2" },
            { "Remote.Username", "" },
            { "Remote.Password", "" },
            { "Remote.MetadataTtlSeconds", "30" },
            { "Remote.NegativeTtlSeconds", "5" },
            { "Remote.DownloadTimeoutSeconds", "600" },
            { "Remote.StoreLocally", "true" },
            { "Remote.Offline", "true" },
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        using var scope = _app.Services.CreateScope();
        var detail = (await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync("up", Cancel))!;
        Assert.Equal(RemoteProtocol.Npm, detail.Settings.Protocol);
        Assert.Equal("http://registry.test/other/", detail.Settings.Url);
        Assert.True(detail.HasPassword);
        Assert.True(detail.Settings.Offline);
    }

    [Fact]
    public async Task TestConnectionAsksTheNpmRegistryWithTheUnsavedValuesAndSavesNothing()
    {
        (await CreateRemoteAsync()).Dispose();

        var fields = RemoteForm("up", change: f => f["Remote.Url"] = "http://typed.test/");
        fields["anonymousRead"] = "true";
        var ok = await ReadAsync(await _admin.PostAsync("admin/repositories/up", fields, action: "admin/repositories/up?handler=Test"));

        Assert.Contains("id=\"remote-test-result\"", ok);
        Assert.Contains("Connected: the npm registry answered.", ok);
        Assert.Equal("http://typed.test/-/ping", _wire.Requests.Single().Uri.ToString());
        using (var scope = _app.Services.CreateScope())
        {
            Assert.Equal(Url, (await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync("up", Cancel))!.Settings.Url);
        }

        // Without the administrator's password nothing is asked.
        fields["password"] = "wrong";
        var refused = await ReadAsync(await _admin.PostAsync("admin/repositories/up", fields, action: "admin/repositories/up?handler=Test"));
        Assert.Contains("confirmation is incorrect", refused);
        Assert.Single(_wire.Requests);
    }

    [Fact]
    public async Task TheCachePanelCountsStoredPackagesAndThePurgesDeleteThem()
    {
        _upstream.Add("left-pad", "1.0.0", "1.1.0");
        _upstream.Add("right-pad", "1.0.0");
        (await CreateRemoteAsync()).Dispose();
        using (var scope = await _app.CreateRepositoryScopeAsync("up", Cancel))
        {
            var registry = scope.ServiceProvider.GetRequiredService<INpmRegistry>();
            foreach (var (name, version) in new[] { ("left-pad", "1.0.0"), ("left-pad", "1.1.0"), ("right-pad", "1.0.0") })
            {
                var tarball = await registry.OpenTarballAsync(name, $"{name}-{version}.tgz", Cancel);
                Assert.NotNull(tarball);
                await tarball!.Content.DisposeAsync();
            }
        }

        var page = await _admin.GetPageAsync("admin/repositories/up");
        Assert.Contains("3 version(s) of 2 package(s)", page);

        var afterOne = await PostThenReadAsync("admin/repositories/up", "PurgePackage", new Dictionary<string, string>
        {
            { "password", Password }, { "packageId", "left-pad" }, { "packageVersion", "1.0.0" },
        });
        Assert.Contains("left-pad 1.0.0 was purged: 1 package version(s) deleted", afterOne);
        Assert.Contains("2 version(s) of 2 package(s)", afterOne);

        var afterAll = await PostThenReadAsync("admin/repositories/up", "Purge", new Dictionary<string, string> { { "password", Password } });
        Assert.Contains("2 package version(s) deleted", afterAll);
        Assert.Contains("0 version(s) of 0 package(s)", afterAll);

        // A name npm would not accept is refused with a message, not stored anywhere.
        var bad = await ReadAsync(await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "password", Password }, { "packageId", "Not A Name" }, { "packageVersion", "" },
        }, action: "admin/repositories/up?handler=PurgePackage"));
        Assert.Contains("lower case", bad);
    }

    // ---- a virtual npm repository ----

    [Fact]
    public async Task AVirtualNpmRepositoryIsCreatedAndOnlyNpmRepositoriesCanBeItsMembers()
    {
        (await CreateRemoteAsync()).Dispose();
        using var created = await CreateVirtualAsync("v");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.EndsWith("/admin/repositories/v", created.Headers.Location!.OriginalString);

        var info = await FindAsync("v");
        Assert.Equal(RepositoryFormat.Npm, info!.Format);
        Assert.Equal(RepositoryKind.Virtual, info.Kind);

        var page = await _admin.GetPageAsync("admin/repositories/v");
        Assert.Contains("Virtual npm repository v was created.", page);
        Assert.Contains("<option value=\"loc\">loc (Local)</option>", page);
        Assert.Contains("<option value=\"up\">up (Remote)</option>", page);
        Assert.DoesNotContain("value=\"nuget-one\"", page);

        // A NuGet repository posted anyway is refused with the reason.
        var refused = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string> { { "member", "nuget-one" } }, action: "admin/repositories/v?handler=AddMember"));
        Assert.Contains("not a Npm repository", refused);

        var list = await _admin.GetPageAsync("admin/repositories");
        Assert.Contains("Npm / Virtual", list);
        Assert.Contains("/r/v/npm/", list);
    }

    [Fact]
    public async Task MembersAreAddedOrderedAndTheDeployRepositoryChosenOnThePage()
    {
        (await CreateRemoteAsync()).Dispose();
        (await CreateVirtualAsync("v")).Dispose();

        await PostThenReadAsync("admin/repositories/v", "AddMember", new Dictionary<string, string> { { "member", "up" } });
        await PostThenReadAsync("admin/repositories/v", "AddMember", new Dictionary<string, string> { { "member", "loc" } });
        await PostThenReadAsync("admin/repositories/v", "MoveMember", new Dictionary<string, string> { { "member", "loc" }, { "direction", "up" } });
        var page = await PostThenReadAsync("admin/repositories/v", "Deploy", new Dictionary<string, string> { { "deploy", "loc" } });

        Assert.Contains("A push to this repository is stored in <strong>loc</strong>", page);
        using var scope = _app.Services.CreateScope();
        var detail = (await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().FindAsync("v", Cancel))!;
        Assert.Equal(new[] { "loc", "up" }, detail.Members.Select(m => m.Name));
        Assert.Equal("loc", detail.DeployRepository);

        // A remote repository cannot receive uploads.
        var refused = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string> { { "deploy", "up" } }, action: "admin/repositories/v?handler=Deploy"));
        Assert.Contains("local repository", refused);
    }

    [Fact]
    public async Task ThePreviewResolvesAnNpmPackageNameAndVersionMemberByMember()
    {
        _upstream.Add("pkg", "2.0.0");
        (await CreateRemoteAsync()).Dispose();
        (await CreateVirtualAsync("v")).Dispose();
        foreach (var member in new[] { "loc", "up" })
        {
            await PostThenReadAsync("admin/repositories/v", "AddMember", new Dictionary<string, string> { { "member", member } });
        }

        var page = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string>
        {
            { "previewPackageId", "pkg" }, { "previewVersion", "2.0.0" },
        }, action: "admin/repositories/v?handler=Preview"));

        Assert.Contains("Package name", page);
        Assert.Contains("data-member=\"loc\" data-outcome=\"NotFound\"", page);
        Assert.Contains("data-member=\"up\" data-outcome=\"WouldFetch\"", page);
        Assert.Contains("comes from <strong>up</strong>", page);
        Assert.Equal(0, _upstream.TarballDownloads);

        var badName = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string>
        {
            { "previewPackageId", "Not A Name" }, { "previewVersion", "1.0.0" },
        }, action: "admin/repositories/v?handler=Preview"));
        Assert.Contains("lower case", badName);

        var badVersion = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string>
        {
            { "previewPackageId", "pkg" }, { "previewVersion", "one" },
        }, action: "admin/repositories/v?handler=Preview"));
        Assert.Contains("not a valid version", badVersion);
    }

    [Fact]
    public async Task TheFormatsPageSaysNpmHasRemoteAndVirtualRepositories()
    {
        var page = await _admin.GetPageAsync("admin/formats");

        Assert.Contains("Local, Remote, Virtual", page);
    }

    [Fact]
    public async Task OnlyAdministratorsCanCreateARemoteOrVirtualNpmRepository()
    {
        await CreateUserAsync(_app, "alice");
        using var alice = new Browser(_app);
        await SignInAsync(alice, "alice");

        using var remote = await alice.RawPostAsync("admin/repositories?handler=Remote", RemoteForm("up"));
        using var virtualRepository = await alice.RawPostAsync("admin/repositories?handler=Virtual", new Dictionary<string, string> { { "name", "v" }, { "format", "Npm" } });

        Assert.NotEqual(HttpStatusCode.OK, remote.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, virtualRepository.StatusCode);
        Assert.Null(await FindAsync("up"));
        Assert.Null(await FindAsync("v"));
    }
}
