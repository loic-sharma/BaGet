using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests.Contracts;

/// <summary>
/// A server seeded for the UI contract: accounts on, validation on, three callers (anonymous, <c>alice</c> a user who owns a
/// package and is member of an organization, <c>root</c> an administrator) and one repository of every kind and format
/// (a second NuGet one, npm, Maven, Docker, a remote and a virtual one) with an artifact in each local one, plus a
/// group, a permission target, a project, a retention policy and a replication configuration.
/// </summary>
internal sealed class UiContractHarness : IDisposable
{
    public const string Package = "Contoso.Lib";

    public BaGetApplication App { get; }
    public Browser Anonymous { get; }
    public Browser Alice { get; }
    public Browser Root { get; }

    private string _aliceKey = string.Empty;

    public UiContractHarness(ITestOutputHelper output)
    {
        var legal = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BaGetTests", "legal-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(legal);
        System.IO.File.WriteAllText(System.IO.Path.Combine(legal, "terms.md"), "# Terms\n\nThese are the terms.\n");
        System.IO.File.WriteAllText(System.IO.Path.Combine(legal, "privacy.md"), "# Privacy\n\nThis is the policy.\n");

        App = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Site:TermsOfUsePath", System.IO.Path.Combine(legal, "terms.md") },
            { "Site:PrivacyPolicyPath", System.IO.Path.Combine(legal, "privacy.md") },
            { "Email:Smtp:Host", "smtp.example.com" },
            { "Email:Smtp:From", "baget@example.com" },
            { "Site:PublicUrl", "https://baget.example.com" },
            { "Scanning:Enabled", "true" },
            { "Scanning:ScanOnDeploy", "false" },
            { "Scanning:OsvUrl", "http://osv.invalid/" },
            { "Accounts:Enabled", "true" },
            { "Accounts:CreateDefaultAdministrator", "false" },
            { "ApiKey", "" },
            { "Validation:Enabled", "true" },
            { "Feeds:EnableV2", "true" },
        });
        Anonymous = new Browser(App);
        Alice = new Browser(App);
        Root = new Browser(App);
        SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Anonymous.Dispose();
        Alice.Dispose();
        Root.Dispose();
        App.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task SeedAsync()
    {
        await CreateUserAsync(App, "alice", email: "alice@example.com");
        await CreateUserAsync(App, "root", administrator: true, email: "root@example.com");
        await AddOrganizationAsync(App, "acme", ("alice", true));
        await SignInAsync(Alice, "alice");
        await SignInAsync(Root, "root");

        using (var scope = App.Services.CreateScope())
        {
            _aliceKey = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
                .CreateAsync((await FindAsync(App, "alice"))!.Id, "contract", ApiKeyScopes.All, "*", null, Token);
        }

        // NuGet: the default repository and a second one.
        using (var package = TestPackages.Build(Package, "1.0.0")) await App.AddPackageAsync(package, Token);
        using (var scope = App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PackageOwnershipService>().AddOwnerAsync(Package, "alice", Token);
        }
        await App.CreateRepositoryAsync("second", Token);
        using (var package = TestPackages.Build("Second.Lib", "1.0.0")) await App.AddPackageAsync(package, Token, "second");

        // npm, Maven, Docker.
        await App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await PublishNpmAsync("left-pad", "1.0.0");
        await App.CreateMavenRepositoryAsync("libs", policy: "Both", cancellationToken: Token);
        await DeployMavenAsync("lib", "1.0");
        await App.CreateDockerRepositoryAsync("reg", cancellationToken: Token);
        await PushDockerAsync("hello", "v1");

        // A remote and a virtual repository.
        using (var scope = App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync(
                "up", RepositoryFormat.NuGet, RepositoryKind.Remote, new RemoteRepositorySettings { Url = "http://upstream.invalid/v3/index.json" }, Token);
        }

        using (await Root.PostAsync("admin/repositories", new Dictionary<string, string> { { "name", "virt" } }, action: "admin/repositories?handler=Virtual"))
        {
        }

        // The access model.
        using (var scope = App.Services.CreateScope())
        {
            var access = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
            await access.CreateGroupAsync("team", null, "contract", Token);
            await access.CreateTargetAsync("t1", new[] { "*" }, null, null, null, "contract", Token);
            await access.CreateProjectAsync("shop", null, "contract", Token);
        }

        // A retention policy and a replication configuration (both are key 1 in a fresh database).
        using (var response = await Root.PostAsync("admin/retention", new Dictionary<string, string>
        {
            { "Form.Name", "p1" }, { "Form.ScopeKind", "repository" }, { "Form.ScopeName", "default" },
            { "Form.Enabled", "true" }, { "Form.KeepLast", "1" }, { "password", Password },
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using (var response = await Root.PostAsync("admin/replication", new Dictionary<string, string>
        {
            { "Form.Name", "r1" }, { "Form.Repository", "default" }, { "Form.Direction", "Push" },
            { "Form.PeerUrl", "http://peer.invalid/" }, { "Form.PeerRepository", "default" }, { "Form.ApiKey", "peer-key" },
            { "Form.Enabled", "false" }, { "Form.Conflict", "TargetWins" }, { "password", Password },
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
    }

    private HttpClient KeyedClient()
    {
        var client = App.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:" + _aliceKey)));

        return client;
    }

    private async Task PublishNpmAsync(string name, string version)
    {
        using var client = App.CreateClient();
        using var request = NpmTestPackages.Json(
            HttpMethod.Put,
            NpmTestPackages.Path("/r/web/npm/", name),
            NpmTestPackages.PublishBody(NpmTestPackages.Tarball(name, version), "latest", "# Readme"),
            _aliceKey);
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task DeployMavenAsync(string artifact, string version)
    {
        var path = $"org/acme/{artifact}/{version}/{artifact}-{version}";
        using var client = KeyedClient();
        foreach (var (extension, body) in new[] { (".jar", "bytes"), (".pom", MavenPom.Generate("org.acme", artifact, version, "jar", "The " + artifact, "About " + artifact)) })
        {
            using var response = await client.PutAsync($"/r/libs/maven/{path}{extension}", new ByteArrayContent(Encoding.UTF8.GetBytes(body)), Token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    private async Task PushDockerAsync(string image, string tag)
    {
        using var client = KeyedClient();
        var content = OciTestImage.Create("contract", 2);
        foreach (var blob in new[] { content.Config }.Concat(content.Layers))
        {
            using var response = await client.PostAsync($"/v2/reg/{image}/blobs/uploads/?digest={OciTestImage.Digest(blob)}", new ByteArrayContent(blob), Token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        using var manifest = new ByteArrayContent(content.Manifest);
        manifest.Headers.ContentType = MediaTypeHeaderValue.Parse(OciTestImage.ManifestType);
        using var put = await client.PutAsync($"/v2/reg/{image}/manifests/{tag}", manifest, Token);
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);
    }
}
