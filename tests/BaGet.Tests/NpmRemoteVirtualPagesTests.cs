using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The web pages of remote and virtual npm repositories (the UI counterpart of the registry): browse and package pages read what the
/// repository serves (a remote one the upstream's packages, a virtual one the merge), the owner tools exist only where packages
/// are owned, and an upload to a virtual repository names the repository it is stored in.
/// </summary>
public class NpmRemoteVirtualPagesTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly FakeNpmUpstream _upstream = new();
    private readonly BaGetApplication _app;
    private readonly Browser _alice;
    private readonly Browser _anonymous;
    private string _aliceKey = string.Empty;

    public NpmRemoteVirtualPagesTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } },
            configureServices: services => services.AddSingleton<IRemoteHttpClientFactory>(new ForwardingRemoteHttpClientFactory(new HttpClient(_upstream))));
        _alice = new Browser(_app);
        _anonymous = new Browser(_app);
        SetUpAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _alice.Dispose();
        _anonymous.Dispose();
        _app.Dispose();
    }

    private async Task SetUpAsync()
    {
        await _app.CreateNpmRepositoryAsync("loc", cancellationToken: Cancel);
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync(
                "up", RepositoryFormat.Npm, RepositoryKind.Remote, new RemoteRepositorySettings { Url = "http://registry.test/" }, anonymousRead: true, Cancel);
            await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().CreateAsync(
                "v", new VirtualRepositorySettings { Members = { "loc", "up" }, DeployRepository = "loc" }, Cancel, actor: null, anonymousRead: true, RepositoryFormat.Npm);
        }

        await CreateUserAsync(_app, "alice");
        await SignInAsync(_alice, "alice");
        using var keys = _app.Services.CreateScope();
        _aliceKey = await keys.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync((await FindAsync(_app, "alice"))!.Id, "t", ApiKeyScopes.All, "*", null, Cancel);
    }

    private async Task PublishAsync(string repository, string name, string version)
    {
        using var client = _app.CreateClient();
        using var request = NpmTestPackages.Json(
            HttpMethod.Put,
            NpmTestPackages.Path($"/r/{repository}/npm/", name),
            NpmTestPackages.PublishBody(NpmTestPackages.Tarball(name, version)),
            _aliceKey);
        using var response = await client.SendAsync(request, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task FetchAsync(string repository, string name, string version)
    {
        using var scope = await _app.CreateRepositoryScopeAsync(repository, Cancel);
        var tarball = await scope.ServiceProvider.GetRequiredService<INpmRegistry>().OpenTarballAsync(name, $"{name}-{version}.tgz", Cancel);
        Assert.NotNull(tarball);
        await tarball!.Content.DisposeAsync();
    }

    // ---- remote ----

    [Fact]
    public async Task TheBrowsePageOfARemoteRepositoryListsWhatItStoredAndItsPackagePageShowsTheUpstreamsVersions()
    {
        _upstream.Add("left-pad", "1.0.0", "1.1.0");
        _upstream.Add("right-pad", "1.0.0");
        await FetchAsync("up", "left-pad", "1.0.0");

        var browse = await _anonymous.GetPageAsync("r/up/npm-packages");
        Assert.Contains("href=\"/r/up/npm-packages/left-pad\"", browse);
        Assert.DoesNotContain("right-pad", browse);

        // The package page reads the upstream's document: all its versions, not only the stored one; no owners, no manage link.
        var page = await _anonymous.GetPageAsync("r/up/npm-packages/left-pad");
        Assert.Contains("id=\"npm-package\"", page);
        Assert.Contains("1.1.0", page);
        Assert.Contains("1.0.0", page);
        Assert.Contains("http://localhost/r/up/npm/left-pad/-/left-pad-1.1.0.tgz", page);
        Assert.DoesNotContain("npm-manage-link", page);

        // A package only the upstream has can be opened too, before anything of it is stored.
        var other = await _anonymous.GetPageAsync("r/up/npm-packages/right-pad");
        Assert.Contains("id=\"npm-package\"", other);
    }

    [Fact]
    public async Task TheManagePageDoesNotExistInARemoteRepositoryWhoseEveryChangeIsRefused()
    {
        _upstream.Add("left-pad", "1.0.0");
        await FetchAsync("up", "left-pad", "1.0.0");

        using var response = await _alice.GetAsync("r/up/npm-manage/left-pad");

        Assert.True(response.StatusCode == HttpStatusCode.NotFound, response.StatusCode + await response.Content.ReadAsStringAsync(Cancel));
    }

    // ---- virtual ----

    [Fact]
    public async Task TheBrowseAndPackagePagesOfAVirtualRepositoryShowTheMergeAndOfferNoOwnerTools()
    {
        await PublishAsync("loc", "left-pad", "1.0.0");
        _upstream.Add("left-pad", "2.0.0");
        _upstream.Add("right-pad", "1.0.0");
        await FetchAsync("up", "right-pad", "1.0.0");

        var browse = await _anonymous.GetPageAsync("r/v/npm-packages");
        Assert.Contains("href=\"/r/v/npm-packages/left-pad\"", browse);
        Assert.Contains("href=\"/r/v/npm-packages/right-pad\"", browse);

        var page = await _anonymous.GetPageAsync("r/v/npm-packages/left-pad");
        Assert.Contains("1.0.0", page);
        Assert.Contains("2.0.0", page);
        // `latest` follows the member order (loc has 1.0.0), and every address points into the virtual repository.
        Assert.Contains("http://localhost/r/v/npm/left-pad/-/left-pad-1.0.0.tgz", page);
        Assert.DoesNotContain("npm-manage-link", page);
        var second = await _anonymous.GetPageAsync("r/v/npm-packages/left-pad?version=2.0.0");
        Assert.Contains("http://localhost/r/v/npm/left-pad/-/left-pad-2.0.0.tgz", second);

        using var manage = await _alice.GetAsync("r/v/npm-manage/left-pad");
        Assert.Equal(HttpStatusCode.NotFound, manage.StatusCode);
    }

    [Fact]
    public async Task AnUploadToAVirtualRepositoryIsStoredInItsDeployRepositoryAndThePageSaysSo()
    {
        var page = await _alice.GetPageAsync("r/v/npm-upload");
        Assert.Contains("Publishes to <strong>loc</strong>", page);
        Assert.Contains("the deploy repository of the virtual repository <strong>v</strong>", page);

        var tarball = NpmTestPackages.Tarball("left-pad", "1.0.0");
        using var response = await _alice.PostFileAsync("r/v/npm-upload", "package", "pkg.tgz", new MemoryStream(tarball.Bytes), new Dictionary<string, string>());
        var html = await response.Content.ReadAsStringAsync(Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Published left-pad@1.0.0.", html);
        using var scope = await _app.CreateRepositoryScopeAsync("loc", Cancel);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<NpmRegistryService>().GetPackageAsync("left-pad", Cancel));
        using var other = await _app.CreateRepositoryScopeAsync("up", Cancel);
        Assert.Null(await other.ServiceProvider.GetRequiredService<NpmRegistryService>().GetPackageAsync("left-pad", Cancel));
    }

    [Fact]
    public async Task ARemoteRepositoryOffersNoUploadAndTheRepositoriesPageLinksTheRightPages()
    {
        using var remoteUpload = await _alice.GetAsync("r/up/npm-upload");
        Assert.Equal(HttpStatusCode.NotFound, remoteUpload.StatusCode);

        await CreateUserAsync(_app, "root", administrator: true);
        using var root = new Browser(_app);
        await SignInAsync(root, "root");
        var list = await root.GetPageAsync("admin/repositories");

        var up = list.Substring(list.IndexOf("data-repository=\"up\"", StringComparison.Ordinal));
        up = up.Substring(0, up.IndexOf("</tr>", StringComparison.Ordinal));
        Assert.Contains("class=\"repository-browse\" href=\"/r/up/npm-packages\"", up);
        Assert.DoesNotContain("repository-upload", up);

        var virtualRow = list.Substring(list.IndexOf("data-repository=\"v\"", StringComparison.Ordinal));
        virtualRow = virtualRow.Substring(0, virtualRow.IndexOf("</tr>", StringComparison.Ordinal));
        Assert.Contains("class=\"repository-browse\" href=\"/r/v/npm-packages\"", virtualRow);
        Assert.Contains("class=\"repository-upload\" href=\"/r/v/npm-upload\"", virtualRow);
    }
}
