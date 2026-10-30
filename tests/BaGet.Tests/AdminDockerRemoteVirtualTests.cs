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
/// The administrator's pages for remote and virtual Docker repositories: the same forms as the other formats', format-aware. Create from
/// the Repositories page (the form asks for the format), edit the upstream, test the connection, look at and purge the cache, add
/// members of the right format and ask where an image tag would come from.
/// </summary>
public class AdminDockerRemoteVirtualTests : IDisposable
{
    private const string Url = "https://" + FakeOciUpstream.RegistryHost;
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly FakeOciUpstream _upstream = new();
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly BaGetApplication _app;
    private readonly Browser _admin;

    public AdminDockerRemoteVirtualTests(ITestOutputHelper output)
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
        _app.CreateMavenRepositoryAsync("maven-one", cancellationToken: Cancel).GetAwaiter().GetResult();
        _app.CreateDockerRepositoryAsync("loc", cancellationToken: Cancel).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
    }

    private static Dictionary<string, string> RemoteForm(string name, string format = "Docker", Action<Dictionary<string, string>>? change = null)
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

    private Task<HttpResponseMessage> CreateRemoteAsync(string name = "up", string format = "Docker", Action<Dictionary<string, string>>? change = null)
        => _admin.PostAsync("admin/repositories", RemoteForm(name, format, change), action: "admin/repositories?handler=Remote");

    private Task<HttpResponseMessage> CreateVirtualAsync(string name, string? format = "Docker")
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

    // Pulls a manifest through the repository, as a Docker client would (the repository then keeps it).
    private async Task PullAsync(string repository, string image, string tag)
    {
        using var client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync($"/v2/{repository}/{image}/manifests/{tag}", Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {image}:{tag}: {response.StatusCode}");
    }

    // ---- the create forms ----

    [Fact]
    public async Task TheCreateFormsOfRemoteAndVirtualRepositoriesOfferDocker()
    {
        var page = await _admin.GetPageAsync("admin/repositories");

        Assert.Matches("<select id=\"remote-format\"[\\s\\S]*?<option value=\"Docker\"", page);
        Assert.Matches("<select id=\"virtual-format\"[\\s\\S]*?<option value=\"Docker\"", page);
        Assert.Contains("registry-1.docker.io", page);
    }

    [Fact]
    public async Task ARemoteDockerRepositoryIsCreatedFromTheFormListedAndOpened()
    {
        using var created = await CreateRemoteAsync(change: f =>
        {
            f["Remote.Username"] = "robot";
            f["Remote.Password"] = "s3cret-pass";
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.EndsWith("/admin/repositories/up", created.Headers.Location!.OriginalString);

        var info = await FindAsync("up");
        Assert.Equal(RepositoryFormat.Docker, info!.Format);
        Assert.Equal(RepositoryKind.Remote, info.Kind);
        using (var scope = _app.Services.CreateScope())
        {
            var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync("up", Cancel);
            Assert.Equal(RemoteProtocol.Docker, detail!.Settings.Protocol);
            Assert.True(detail.HasPassword);
        }

        var list = await _admin.GetPageAsync("admin/repositories");
        Assert.Contains("Remote Docker repository up was created.", list);
        Assert.Contains("Docker / Remote", list);

        var page = await _admin.GetPageAsync("admin/repositories/up");
        Assert.Contains("id=\"remote-protocol-docker\"", page);
        Assert.DoesNotMatch("<select[^>]*Remote_Protocol", page);
        Assert.Contains("https://registry-1.docker.io", page);
        Assert.Contains("A password is stored", page);
        Assert.DoesNotContain("s3cret-pass", page);
        Assert.Contains("id=\"remote-test\"", page);
        Assert.Contains("id=\"remote-purge-form\"", page);
        Assert.Contains("a remote repository takes no deployments", page);
        Assert.Contains("href=\"/r/up/docker-images\"", page);
        Assert.Contains("Image name", page);
        Assert.Contains("Tag or digest", page);
    }

    [Fact]
    public async Task InvalidRegistrySettingsAreRefusedWithAMessage()
    {
        var html = await ReadAsync(await CreateRemoteAsync(change: f => f["Remote.Url"] = Url + "/?x=1"));

        Assert.Contains("id=\"repositories-error\"", html);
        Assert.Contains("must not have a query string", html);
        Assert.Null(await FindAsync("up"));
    }

    // ---- editing a remote Docker repository ----

    [Fact]
    public async Task SavingKeepsTheDockerProtocolAndTheStoredPassword()
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
            { "Remote.Url", Url },
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
        Assert.Equal(RemoteProtocol.Docker, detail.Settings.Protocol);
        Assert.True(detail.HasPassword);
        Assert.True(detail.Settings.Offline);
    }

    [Fact]
    public async Task TestConnectionAsksTheRegistryWithTheUnsavedValuesAndSavesNothing()
    {
        (await CreateRemoteAsync()).Dispose();

        var fields = RemoteForm("up", change: f => f["Remote.Url"] = "https://typed.test");
        fields["anonymousRead"] = "true";
        var ok = await ReadAsync(await _admin.PostAsync("admin/repositories/up", fields, action: "admin/repositories/up?handler=Test"));

        Assert.Contains("id=\"remote-test-result\"", ok);
        Assert.Contains("Connected: the registry answered the version check", ok);
        Assert.Contains(_wire.Requests, r => r.Uri.Host == "typed.test" && r.Uri.AbsolutePath == "/v2/");
        using (var scope = _app.Services.CreateScope())
        {
            Assert.Equal(Url, (await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync("up", Cancel))!.Settings.Url);
        }

        // Without the administrator's password nothing is asked.
        var before = _wire.Requests.Length;
        fields["password"] = "wrong";
        var refused = await ReadAsync(await _admin.PostAsync("admin/repositories/up", fields, action: "admin/repositories/up?handler=Test"));
        Assert.Contains("confirmation is incorrect", refused);
        Assert.Equal(before, _wire.Requests.Length);
    }

    [Fact]
    public async Task TheCachePanelCountsStoredImagesAndThePurgesDeleteThem()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        _upstream.AddImage("team/app", "2.0", "b");
        _upstream.AddImage("team/other", "1.0", "c");
        (await CreateRemoteAsync()).Dispose();
        await PullAsync("up", "team/app", "1.0");
        await PullAsync("up", "team/app", "2.0");
        await PullAsync("up", "team/other", "1.0");

        var page = await _admin.GetPageAsync("admin/repositories/up");
        Assert.Contains("Stored images", page);
        Assert.Contains("3 manifest(s) of 2 image(s)", page);

        // A tag purges its manifest; a name that is not an image is refused with a message.
        var afterOne = await PostThenReadAsync("admin/repositories/up", "PurgePackage", new Dictionary<string, string>
        {
            { "password", Password }, { "packageId", "team/app" }, { "packageVersion", "2.0" },
        });
        Assert.Contains("team/app 2.0 was purged: 1 manifest(s) deleted", afterOne);
        Assert.Contains("2 manifest(s) of 2 image(s)", afterOne);

        var bad = await ReadAsync(await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "password", Password }, { "packageId", "Not An Image" }, { "packageVersion", "" },
        }, action: "admin/repositories/up?handler=PurgePackage"));
        Assert.Contains("is not an image name", bad);

        var afterAll = await PostThenReadAsync("admin/repositories/up", "Purge", new Dictionary<string, string> { { "password", Password } });
        Assert.Contains("2 manifest(s) deleted", afterAll);
        Assert.Contains("0 manifest(s) of 0 image(s)", afterAll);
    }

    // ---- a virtual Docker repository ----

    [Fact]
    public async Task AVirtualDockerRepositoryIsCreatedAndOnlyDockerRepositoriesCanBeItsMembers()
    {
        (await CreateRemoteAsync()).Dispose();
        using var created = await CreateVirtualAsync("v");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.EndsWith("/admin/repositories/v", created.Headers.Location!.OriginalString);

        var info = await FindAsync("v");
        Assert.Equal(RepositoryFormat.Docker, info!.Format);
        Assert.Equal(RepositoryKind.Virtual, info.Kind);

        var page = await _admin.GetPageAsync("admin/repositories/v");
        Assert.Contains("Virtual Docker repository v was created.", page);
        Assert.Contains("<option value=\"loc\">loc (Local)</option>", page);
        Assert.Contains("<option value=\"up\">up (Remote)</option>", page);
        Assert.DoesNotContain("value=\"nuget-one\"", page);
        Assert.DoesNotContain("value=\"maven-one\"", page);

        var refused = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string> { { "member", "maven-one" } }, action: "admin/repositories/v?handler=AddMember"));
        Assert.Contains("not a Docker repository", refused);

        var list = await _admin.GetPageAsync("admin/repositories");
        Assert.Contains("Docker / Virtual", list);
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

        var refused = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string> { { "deploy", "up" } }, action: "admin/repositories/v?handler=Deploy"));
        Assert.Contains("local repository", refused);
    }

    [Fact]
    public async Task ThePreviewResolvesAnImageTagMemberByMember()
    {
        _upstream.AddImage("team/app", "2.0", "b");
        (await CreateRemoteAsync()).Dispose();
        (await CreateVirtualAsync("v")).Dispose();
        foreach (var member in new[] { "loc", "up" })
        {
            await PostThenReadAsync("admin/repositories/v", "AddMember", new Dictionary<string, string> { { "member", member } });
        }

        var page = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string>
        {
            { "previewPackageId", "team/app" }, { "previewVersion", "2.0" },
        }, action: "admin/repositories/v?handler=Preview"));

        Assert.Contains("Image name", page);
        Assert.Contains("Tag or digest", page);
        Assert.Contains("data-member=\"loc\" data-outcome=\"NotFound\"", page);
        Assert.Contains("data-member=\"up\" data-outcome=\"WouldFetch\"", page);
        Assert.Contains("team/app 2.0 comes from <strong>up</strong>", page);

        // Asking is not pulling: nothing was fetched or kept.
        Assert.Equal(0, _upstream.Count("GET", "/manifests/2.0"));

        var badName = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string>
        {
            { "previewPackageId", "Team App" }, { "previewVersion", "1.0" },
        }, action: "admin/repositories/v?handler=Preview"));
        Assert.Contains("is not an image name", badName);

        var badTag = await ReadAsync(await _admin.PostAsync("admin/repositories/v", new Dictionary<string, string>
        {
            { "previewPackageId", "team/app" }, { "previewVersion", "1 0" },
        }, action: "admin/repositories/v?handler=Preview"));
        Assert.Contains("is not a tag or a sha256 digest", badTag);
    }

    [Fact]
    public async Task TheFormatsPageSaysDockerHasRemoteAndVirtualRepositories()
    {
        var page = await _admin.GetPageAsync("admin/formats");

        Assert.Matches("Docker[\\s\\S]*?Local, Remote, Virtual", page);
    }

    [Fact]
    public async Task OnlyAdministratorsCanCreateARemoteOrVirtualDockerRepository()
    {
        await CreateUserAsync(_app, "alice");
        using var alice = new Browser(_app);
        await SignInAsync(alice, "alice");

        using var remote = await alice.RawPostAsync("admin/repositories?handler=Remote", RemoteForm("up"));
        using var virtualRepository = await alice.RawPostAsync("admin/repositories?handler=Virtual", new Dictionary<string, string> { { "name", "v" }, { "format", "Docker" } });

        Assert.NotEqual(HttpStatusCode.OK, remote.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, virtualRepository.StatusCode);
        Assert.Null(await FindAsync("up"));
        Assert.Null(await FindAsync("v"));
    }
}
