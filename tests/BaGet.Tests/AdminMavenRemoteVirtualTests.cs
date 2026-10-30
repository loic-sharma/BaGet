using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// The administrator's pages for remote and virtual Maven repositories: the same forms as NuGet's and npm's, format-aware. Create from
/// the Repositories page (the form asks for the format), edit the upstream, test the connection, look at and purge the cache, add
/// members of the right format and ask where an artifact version would come from.
/// </summary>
public class AdminMavenRemoteVirtualTests : IDisposable
{
    private const string Url = "http://repo.test/maven2/";
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly FakeMavenUpstream _upstream = new();
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly BaGetApplication _app;
    private readonly Browser _admin;

    public AdminMavenRemoteVirtualTests(ITestOutputHelper output)
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
        _app.CreateNpmRepositoryAsync("npm-one", cancellationToken: Cancel).GetAwaiter().GetResult();
        _app.CreateMavenRepositoryAsync("loc", cancellationToken: Cancel).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
    }

    private static Dictionary<string, string> RemoteForm(string name, string format = "Maven", Action<Dictionary<string, string>>? change = null)
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

    private Task<HttpResponseMessage> CreateRemoteAsync(string name = "up", string format = "Maven", Action<Dictionary<string, string>>? change = null)
        => _admin.PostAsync("admin/repositories", RemoteForm(name, format, change), action: "admin/repositories?handler=Remote");

    private Task<HttpResponseMessage> CreateVirtualAsync(string name, string? format = "Maven")
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

    // Requests a file through the repository, as a build tool would (the repository then keeps it).
    private async Task FetchAsync(string repository, string path)
    {
        using var client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync($"/r/{repository}/maven/{path}", Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path}: {response.StatusCode}");
    }

    // ---- the create forms ----

    [Fact]
    public async Task TheCreateFormsOfRemoteAndVirtualRepositoriesOfferMaven()
    {
        var page = await _admin.GetPageAsync("admin/repositories");

        Assert.Matches("<select id=\"remote-format\"[\\s\\S]*?<option value=\"Maven\"", page);
        Assert.Matches("<select id=\"virtual-format\"[\\s\\S]*?<option value=\"Maven\"", page);
        Assert.Contains("repo.maven.apache.org/maven2", page);
    }

    [Fact]
    public async Task ARemoteMavenRepositoryIsCreatedFromTheFormListedAndOpened()
    {
        using var created = await CreateRemoteAsync(change: f =>
        {
            f["Remote.Username"] = "robot";
            f["Remote.Password"] = "s3cret-pass";
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.EndsWith("/admin/repositories/up", created.Headers.Location!.OriginalString);

        var info = await FindAsync("up");
        Assert.Equal(RepositoryFormat.Maven, info!.Format);
        Assert.Equal(RepositoryKind.Remote, info.Kind);
        using (var scope = _app.Services.CreateScope())
        {
            var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync("up", Cancel);
            Assert.Equal(RemoteProtocol.Maven, detail!.Settings.Protocol);
            Assert.True(detail.HasPassword);
        }

        var list = await _admin.GetPageAsync("admin/repositories");
        Assert.Contains("Remote Maven repository up was created.", list);
        Assert.Contains("Maven / Remote", list);
        Assert.Contains("/r/up/maven/", list);

        var page = await _admin.GetPageAsync("admin/repositories/up");
        Assert.Contains("id=\"remote-protocol-maven\"", page);
        Assert.DoesNotMatch("<select[^>]*Remote_Protocol", page);
        Assert.Contains("https://repo.maven.apache.org/maven2/", page);
        Assert.Contains("A password is stored", page);
        Assert.DoesNotContain("s3cret-pass", page);
        Assert.Contains("id=\"remote-test\"", page);
        Assert.Contains("id=\"remote-purge-form\"", page);
        Assert.Contains("a remote repository takes no deployments", page);
        Assert.Contains("href=\"/r/up/maven-artifacts\"", page);
        Assert.DoesNotContain("id=\"maven-settings-form\"", page);
    }

    [Fact]
    public async Task InvalidMavenUpstreamSettingsAreRefusedWithAMessage()
    {
        var html = await ReadAsync(await CreateRemoteAsync(change: f => f["Remote.Url"] = "http://repo.test/maven2/?x=1"));

        Assert.Contains("id=\"repositories-error\"", html);
        Assert.Contains("must not have a query string", html);
        Assert.Null(await FindAsync("up"));
    }

    // ---- editing a remote Maven repository ----

    [Fact]
    public async Task SavingKeepsTheMavenProtocolAndTheStoredPassword()
    {
        (await CreateRemoteAsync(change: f =>
        {
            f["Remote.Username"] = "robot";
            f["Remote.Password"] = "tok";
        })).Dispose();

        using var saved = await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "anonymousRead", "true" },
            { "password", Password },
            { "Remote.Url", "http://repo.test/other/" },
            { "Remote.Protocol", "V2" },
            { "Remote.Username", "robot" },
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
        Assert.Equal(RemoteProtocol.Maven, detail.Settings.Protocol);
        Assert.Equal("http://repo.test/other/", detail.Settings.Url);
        Assert.True(detail.HasPassword);
        Assert.True(detail.Settings.Offline);
    }

    [Fact]
    public async Task TestConnectionAsksTheMavenRepositoryWithTheUnsavedValuesAndSavesNothing()
    {
        (await CreateRemoteAsync()).Dispose();

        var fields = RemoteForm("up", change: f => f["Remote.Url"] = "http://typed.test/maven2/");
        fields["anonymousRead"] = "true";
        var ok = await ReadAsync(await _admin.PostAsync("admin/repositories/up", fields, action: "admin/repositories/up?handler=Test"));

        Assert.Contains("id=\"remote-test-result\"", ok);
        Assert.Contains("Connected: the Maven repository answered.", ok);
        Assert.Equal("http://typed.test/maven2/", _wire.Requests.Single().Uri.ToString());
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
    public async Task TheCachePanelCountsStoredArtifactsAndThePurgesDeleteThem()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        _upstream.AddRelease("org.acme", "lib", "5.3.20.RELEASE");
        _upstream.AddRelease("org.acme", "other", "1.0");
        (await CreateRemoteAsync()).Dispose();
        await FetchAsync("up", "org/acme/lib/1.0/lib-1.0.jar");
        await FetchAsync("up", "org/acme/lib/5.3.20.RELEASE/lib-5.3.20.RELEASE.jar");
        await FetchAsync("up", "org/acme/other/1.0/other-1.0.jar");

        var page = await _admin.GetPageAsync("admin/repositories/up");
        Assert.Contains("Stored artifacts", page);
        Assert.Contains("3 version(s) of 2 artifact(s)", page);
        Assert.Contains("Artifact (group:artifact)", page);

        // A Maven version is any version folder name, not always a NuGet version.
        var afterOne = await PostThenReadAsync("admin/repositories/up", "PurgePackage", new Dictionary<string, string>
        {
            { "password", Password }, { "packageId", "org.acme:lib" }, { "packageVersion", "5.3.20.RELEASE" },
        });
        Assert.Contains("org.acme:lib 5.3.20.RELEASE was purged: 1 file(s) deleted", afterOne);
        Assert.Contains("2 version(s) of 2 artifact(s)", afterOne);

        var afterAll = await PostThenReadAsync("admin/repositories/up", "Purge", new Dictionary<string, string> { { "password", Password } });
        Assert.Contains("2 package version(s) deleted", afterAll);
        Assert.Contains("0 version(s) of 0 artifact(s)", afterAll);

        // Something that is not group:artifact is refused with a message.
        var bad = await ReadAsync(await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "password", Password }, { "packageId", "lib" }, { "packageVersion", "" },
        }, action: "admin/repositories/up?handler=PurgePackage"));
        Assert.Contains("write it as group:artifact", bad);
    }

    // ---- a virtual Maven repository ----

    [Fact]
    public async Task AVirtualMavenRepositoryIsCreatedAndOnlyMavenRepositoriesCanBeItsMembers()
    {
        (await CreateRemoteAsync()).Dispose();
        using var created = await CreateVirtualAsync("v");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.EndsWith("/admin/repositories/v", created.Headers.Location!.OriginalString);

        var info = await FindAsync("v");
        Assert.Equal(RepositoryFormat.Maven, info!.Format);
        Assert.Equal(RepositoryKind.Virtual, info.Kind);

        var page = await _admin.GetPageAsync("admin/repositories/v");
        Assert.Contains("Virtual Maven repository v was created.", page);
        Assert.Contains("<option value=\"loc\">loc (Local)</option>", page);
        Assert.Contains("<option value=\"up\">up (Remote)</option>", page);
        Assert.DoesNotContain("value=\"nuget-one\"", page);
        Assert.DoesNotContain("value=\"npm-one\"", page);

        // An npm repository posted anyway is refused with the reason.
        var refused = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string> { { "member", "npm-one" } }, action: "admin/repositories/v?handler=AddMember"));
        Assert.Contains("not a Maven repository", refused);

        var list = await _admin.GetPageAsync("admin/repositories");
        Assert.Contains("Maven / Virtual", list);
        Assert.Contains("/r/v/maven/", list);
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
        Assert.Contains("<code>latest</code> and <code>release</code> follow the member order", page);
        using var scope = _app.Services.CreateScope();
        var detail = (await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().FindAsync("v", Cancel))!;
        Assert.Equal(new[] { "loc", "up" }, detail.Members.Select(m => m.Name));
        Assert.Equal("loc", detail.DeployRepository);

        // A remote repository cannot receive uploads.
        var refused = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string> { { "deploy", "up" } }, action: "admin/repositories/v?handler=Deploy"));
        Assert.Contains("local repository", refused);
    }

    [Fact]
    public async Task ThePreviewResolvesAMavenArtifactAndVersionMemberByMember()
    {
        _upstream.AddRelease("org.acme", "lib", "5.3.20.RELEASE");
        (await CreateRemoteAsync()).Dispose();
        (await CreateVirtualAsync("v")).Dispose();
        foreach (var member in new[] { "loc", "up" })
        {
            await PostThenReadAsync("admin/repositories/v", "AddMember", new Dictionary<string, string> { { "member", member } });
        }

        var page = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string>
        {
            { "previewPackageId", "org.acme:lib" }, { "previewVersion", "5.3.20.RELEASE" },
        }, action: "admin/repositories/v?handler=Preview"));

        Assert.Contains("Artifact (group:artifact)", page);
        Assert.Contains("data-member=\"loc\" data-outcome=\"NotFound\"", page);
        Assert.Contains("data-member=\"up\" data-outcome=\"WouldFetch\"", page);
        Assert.Contains("org.acme:lib 5.3.20.RELEASE comes from <strong>up</strong>", page);
        Assert.Equal(0, _upstream.Gets("lib-5.3.20.RELEASE.jar"));

        var badName = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string>
        {
            { "previewPackageId", "lib" }, { "previewVersion", "1.0" },
        }, action: "admin/repositories/v?handler=Preview"));
        Assert.Contains("write it as group:artifact", badName);

        var badVersion = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string>
        {
            { "previewPackageId", "org.acme:lib" }, { "previewVersion", "1 0" },
        }, action: "admin/repositories/v?handler=Preview"));
        Assert.Contains("not a valid version", badVersion);
    }

    [Fact]
    public async Task TheFormatsPageSaysMavenHasRemoteAndVirtualRepositories()
    {
        var page = await _admin.GetPageAsync("admin/formats");

        Assert.Matches("Maven[\\s\\S]*?Local, Remote, Virtual", page);
    }

    [Fact]
    public async Task OnlyAdministratorsCanCreateARemoteOrVirtualMavenRepository()
    {
        await CreateUserAsync(_app, "alice");
        using var alice = new Browser(_app);
        await SignInAsync(alice, "alice");

        using var remote = await alice.RawPostAsync("admin/repositories?handler=Remote", RemoteForm("up"));
        using var virtualRepository = await alice.RawPostAsync("admin/repositories?handler=Virtual", new Dictionary<string, string> { { "name", "v" }, { "format", "Maven" } });

        Assert.NotEqual(HttpStatusCode.OK, remote.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, virtualRepository.StatusCode);
        Assert.Null(await FindAsync("up"));
        Assert.Null(await FindAsync("v"));
    }
}
