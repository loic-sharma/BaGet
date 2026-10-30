using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Validation.Pipeline;

namespace BaGet.Tests;

/// <summary>
/// UI coverage audit (docs/ui-coverage.md): server capabilities that had no page. The validation status a pusher reads over
/// the API (<c>v3/package/{id}/{version}/validation</c>) is a page with the same gate, and the administrator's repository list
/// links every protocol resource a NuGet repository serves, not only the service index.
/// </summary>
public class UiCoverageTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _alice;
    private readonly Browser _bob;
    private readonly Browser _admin;

    public UiCoverageTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "Accounts:Enabled", "true" },
                { "ApiKey", "" },
                { "Validation:Enabled", "true" },
            },
            configureServices: services => services.AddSingleton<IPackageValidator>(new RejectingValidator()));
        _alice = new Browser(_app);
        _bob = new Browser(_app);
        _admin = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        CreateUserAsync(_app, "alice").GetAwaiter().GetResult();
        CreateUserAsync(_app, "bob").GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
        SignInAsync(_alice, "alice").GetAwaiter().GetResult();
        SignInAsync(_bob, "bob").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _alice.Dispose();
        _bob.Dispose();
        _admin.Dispose();
        _app.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task PushQuarantinedAsync(string id)
    {
        string key;
        using (var scope = _app.Services.CreateScope())
        {
            var user = await FindAsync(_app, "alice");
            key = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
                .CreateAsync(user!.Id, "test", new[] { ApiKeyScopes.Push }, "*", null, Token);
        }

        using var client = _app.CreateClient();
        using var package = TestPackages.Build(id, "1.0.0");
        using var request = TestPackages.PushRequest(package, key);
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            using var scope = _app.Services.CreateScope();
            var queue = await scope.ServiceProvider.GetRequiredService<IValidationAdministration>().ListAsync(Token);
            if (queue.Any(s => s.PackageId == id && s.State == ValidationSubmissionState.Quarantined)) return;
            Assert.True(DateTime.UtcNow < deadline, $"{id} was not quarantined");
            await Task.Delay(50, Token);
        }
    }

    [Fact]
    public async Task ThePusherReadsTheValidationStatusOfASubmissionInTheBrowser()
    {
        await PushQuarantinedAsync("Quarantined.Pkg");

        using var response = await _alice.GetAsync("packages/Quarantined.Pkg/validation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(Token);
        Assert.Contains("Quarantined", html);
        Assert.Contains("1.0.0", html);
        Assert.Contains("Rejected by the test.", html);
    }

    [Fact]
    public async Task ManageLinksToTheValidationStatus()
    {
        await PushQuarantinedAsync("Quarantined.Pkg");

        var html = await _alice.GetPageAsync("packages/Quarantined.Pkg/manage");

        Assert.Contains("packages/Quarantined.Pkg/validation", html);
    }

    [Fact]
    public async Task AStrangerCannotReadTheValidationStatus()
    {
        await PushQuarantinedAsync("Quarantined.Pkg");

        using var bob = await _bob.GetAsync("packages/Quarantined.Pkg/validation");
        using var anonymous = await _app.CreateClient().GetAsync("packages/Quarantined.Pkg/validation", Token);

        Assert.Equal(HttpStatusCode.Forbidden, bob.StatusCode);
        // Sent to sign in (the client follows the redirect): never the page.
        Assert.DoesNotContain("Rejected by the test.", await anonymous.Content.ReadAsStringAsync(Token));
        Assert.Contains("login", anonymous.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnAdministratorReadsAnyValidationStatusAndAnUnknownIdIsNotFound()
    {
        await PushQuarantinedAsync("Quarantined.Pkg");

        using var admin = await _admin.GetAsync("packages/Quarantined.Pkg/validation");
        using var unknown = await _admin.GetAsync("packages/Nothing.Here/validation");

        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task TheRepositoryListLinksEveryProtocolResourceOfANuGetRepository()
    {
        var html = await _admin.GetPageAsync("admin/repositories");

        foreach (var id in new[] { "repository-v2", "repository-catalog", "repository-vulnerabilities", "repository-signatures" })
        {
            Assert.Contains(id, html);
        }

        Assert.Contains("api/v2", html);
        Assert.Contains("v3/catalog/index.json", html);
        Assert.Contains("v3/vulnerabilities/index.json", html);
        Assert.Contains("v3/repository-signatures/index.json", html);
    }

    [Fact]
    public async Task TheSearchBoxesOfferTypeAheadFromTheAutocompleteResource()
    {
        var home = await _admin.GetPageAsync("");
        Assert.Contains("data-autocomplete=\"", home);
        Assert.Contains("v3/autocomplete", home);
        Assert.Contains("list=\"home-package-suggestions\"", home);

        var results = await _admin.GetPageAsync("?q=a");
        Assert.Contains("list=\"package-suggestions\"", results);
    }

    private sealed class RejectingValidator : IPackageValidator
    {
        public string Name => "Toggle";

        public Task<PackageValidatorResult> ValidateAsync(PackageValidationContext context, CancellationToken cancellationToken)
            => Task.FromResult(PackageValidatorResult.Invalid("Rejected by the test."));
    }
}
