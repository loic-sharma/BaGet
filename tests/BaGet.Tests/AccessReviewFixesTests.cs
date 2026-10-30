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
using Microsoft.Extensions.Logging;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Organizations;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>
/// Review fixes of the access control work: the upload page needs Deploy (not Read) so a drop box is usable, the
/// validation status is exempt from the read check, deleting an organization cleans the access model, and the upgrade
/// warning for repositories that became private.
/// </summary>
public class AccessReviewFixesTests : IDisposable
{
    private const string Second = "second";

    private readonly BaGetApplication _app;
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;

    public AccessReviewFixesTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", "global-key" },
            { "Accounts:Enabled", "true" },
            { "Validation:Enabled", "true" },
        });
    }

    public void Dispose() => _app.Dispose();

    private async Task<string> KeyAsync(string user)
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync((await FindAsync(_app, user))!.Id, "test", ApiKeyScopes.All, "*", null, CancellationToken.None);
    }

    /// <summary><c>second</c> is private; dave holds Deploy only (a drop box), erin holds Read only, bob nothing.</summary>
    private async Task SetUpDropBoxAsync()
    {
        await _app.CreateRepositoryAsync(Second, _token);
        foreach (var name in new[] { "dave", "erin", "bob" }) await CreateUserAsync(_app, name);

        using var scope = _app.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
        await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync(Second, false, _token);
        await administration.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, null, _token);
        await administration.CreateTargetAsync("drop box", new[] { Second }, null, null, null, null, _token);
        await administration.SetGrantAsync("drop box", PrincipalType.User, (await FindAsync(_app, "dave"))!.Id, AccessAction.Deploy, null, _token);
        await administration.CreateTargetAsync("readers", new[] { Second }, null, null, null, null, _token);
        await administration.SetGrantAsync("readers", PrincipalType.User, (await FindAsync(_app, "erin"))!.Id, AccessAction.Read, null, _token);
    }

    [Fact]
    public async Task TheUploadPageOpensForADeployOnlyUserAndNotForOneWithoutDeploy()
    {
        await SetUpDropBoxAsync();

        using var dave = new Browser(_app);
        await SignInAsync(dave, "dave");
        using var drop = await dave.GetAsync("r/second/upload");
        Assert.Equal(HttpStatusCode.OK, drop.StatusCode);

        // Read without Deploy cannot publish, bob has nothing: both answer 404 like an unknown repository.
        foreach (var name in new[] { "erin", "bob" })
        {
            using var browser = new Browser(_app);
            await SignInAsync(browser, name);
            using var denied = await browser.GetAsync("r/second/upload");
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
    }

    [Fact]
    public async Task ADeployOnlyUserCanPushAndSeeTheValidationStatusOfWhatItPushed()
    {
        await SetUpDropBoxAsync();
        var daveKey = await KeyAsync("dave");
        var bobKey = await KeyAsync("bob");
        using var client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using (var package = TestPackages.Build("Drop.Lib", "1.0.0"))
        using (var push = TestPackages.PushRequest(package, daveKey))
        {
            push.RequestUri = new Uri("r/second/api/v2/package", UriKind.Relative);
            using var pushed = await client.SendAsync(push, _token);
            Assert.True(pushed.IsSuccessStatusCode, pushed.StatusCode.ToString());
        }

        async Task<HttpStatusCode> StatusAsync(string? key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "r/second/v3/package/Drop.Lib/1.0.0/validation");
            if (key != null) request.Headers.Add("X-NuGet-ApiKey", key);
            using var response = await client.SendAsync(request, _token);
            return response.StatusCode;
        }

        // The pusher has no Read, yet sees the status: the route authorizes per repository and package itself.
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(daveKey));
        // Anyone else gets the controller's own answer, never package data.
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(null));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(bobKey));
    }

    [Fact]
    public async Task DeletingAnOrganizationRemovesItsGrantsAndRolesAuditsAndInvalidatesTheCache()
    {
        await CreateUserAsync(_app, "root", administrator: true);
        await CreateUserAsync(_app, "alice");
        await AddOrganizationAsync(_app, "contoso", ("alice", true));
        var organization = await WithAsync(_app, sp =>
            Task.FromResult(sp.GetRequiredService<IContext>().Organizations!.Single(o => o.Name == "contoso").Key.ToString()));

        await WithAsync(_app, async sp =>
        {
            var administration = sp.GetRequiredService<IAccessAdministration>();
            await administration.CreateTargetAsync("contoso target", new[] { "*" }, null, null, null, null, _token);
            await administration.SetGrantAsync("contoso target", PrincipalType.Organization, organization, AccessAction.Read, null, _token);
            var project = await administration.CreateProjectAsync("proj", null, "test", _token);
            var context = sp.GetRequiredService<IContext>();
            context.ProjectMembers!.Add(new ProjectMember
            {
                ProjectKey = project.Key,
                PrincipalType = PrincipalType.Organization,
                PrincipalId = organization,
                Role = ProjectRole.Developer,
            });
            await context.SaveChangesAsync(_token);
            return 0;
        });

        var before = _app.Services.GetRequiredService<AccessModelVersion>().Current;
        var change = await WithAsync(_app, sp => sp.GetRequiredService<PackageOwnershipService>().DeleteOrganizationAsync("contoso", _token, "root"));
        Assert.Equal(OrganizationChange.Success, change);

        await WithAsync(_app, async sp =>
        {
            var context = sp.GetRequiredService<IContext>();
            Assert.Empty(context.PermissionGrants!.Where(g => g.PrincipalType == PrincipalType.Organization && g.PrincipalId == organization));
            Assert.Empty(context.ProjectMembers!.Where(m => m.PrincipalType == PrincipalType.Organization && m.PrincipalId == organization));
            var entry = Assert.Single(await sp.GetRequiredService<IAccessAdministration>().AuditAsync(50, _token), e => e.Action == "organization.delete");
            Assert.Equal("contoso", entry.Subject);
            Assert.Equal("root", entry.Actor);
            return 0;
        });

        Assert.True(_app.Services.GetRequiredService<AccessModelVersion>().Current > before);
    }

    [Fact]
    public async Task TheUpgradeWarningNamesPrivateRepositoriesNobodyCanRead()
    {
        foreach (var name in new[] { "locked", "open", "shared" }) await _app.CreateRepositoryAsync(name, _token);
        await CreateUserAsync(_app, "alice");

        using var scope = _app.Services.CreateScope();
        var repositoriesAdmin = scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>();
        await repositoriesAdmin.SetAnonymousReadAsync("locked", false, _token);
        await repositoriesAdmin.SetAnonymousReadAsync("shared", false, _token);
        var administration = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
        await administration.CreateTargetAsync("shared readers", new[] { "shared" }, null, null, null, null, _token);
        await administration.SetGrantAsync("shared readers", PrincipalType.User, (await FindAsync(_app, "alice"))!.Id, AccessAction.Read, null, _token);

        var logger = new CapturingLogger();
        Task WarnAsync() => AccessUpgradeCheck.WarnAsync(
            scope.ServiceProvider.GetRequiredService<IRepositoryService>(),
            scope.ServiceProvider.GetRequiredService<IAccessEvaluator>(),
            logger,
            _token);

        await WarnAsync();

        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("locked", warning.Message);
        Assert.DoesNotContain("shared", warning.Message);
        Assert.DoesNotContain("open,", warning.Message);

        // Switching the repository on silences it.
        await repositoriesAdmin.SetAnonymousReadAsync("locked", true, _token);
        logger.Entries.Clear();
        await WarnAsync();
        Assert.Empty(logger.Entries);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
