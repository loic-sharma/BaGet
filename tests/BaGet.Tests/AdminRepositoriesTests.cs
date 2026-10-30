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
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// The administrator's Repositories pages: list with feed addresses, create, edit, delete. Admin only; the default
/// repository is protected; a repository with packages cannot be deleted; writes are refused while the maintenance
/// gate is closed.
/// </summary>
public class AdminRepositoriesTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _admin;

    public AdminRepositoriesTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
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

    private Task<HttpResponseMessage> CreateAsync(string name)
        => _admin.PostAsync("admin/repositories", new Dictionary<string, string> { { "name", name } });

    private async Task<RepositorySummary?> FindAsync(string name)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().FindAsync(name, CancellationToken.None);
    }

    private async Task SeedPackageAsync(string repository, string id)
    {
        using var scope = _app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var info = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository, CancellationToken.None);
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use(info!);
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        context.Packages!.Add(new Package
        {
            Id = id,
            Version = NuGet.Versioning.NuGetVersion.Parse("1.0.0"),
            Listed = true,
            Authors = Array.Empty<string>(),
            Tags = Array.Empty<string>(),
            Dependencies = new List<PackageDependency>(),
            PackageTypes = new List<PackageType>(),
            TargetFrameworks = new List<TargetFramework>(),
        });
        await context.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AnonymousAndNonAdministratorsDoNotSeeThePages()
    {
        await CreateUserAsync(_app, "alice");
        using var alice = new Browser(_app);
        await SignInAsync(alice, "alice");
        using var anonymous = new Browser(_app);

        foreach (var path in new[] { "admin/repositories", "admin/repositories/default", "admin/repositories/default/delete" })
        {
            using var asUser = await alice.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, asUser.StatusCode);

            using var asAnonymous = await anonymous.GetAsync(path);
            Assert.NotEqual(HttpStatusCode.OK, asAnonymous.StatusCode);
        }

        using var post = await alice.RawPostAsync("admin/repositories", new Dictionary<string, string> { { "name", "second" } });
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        Assert.Null(await FindAsync("second"));
    }

    [Fact]
    public async Task TheListShowsTheDefaultRepositoryWithItsFeedAndTheMenuLinksToIt()
    {
        var html = await _admin.GetPageAsync("admin/repositories");

        Assert.Contains("data-repository=\"default\"", html);
        Assert.Contains("/v3/index.json", html);
        Assert.DoesNotContain("/r/default/", html);
        Assert.DoesNotContain("admin/repositories/default/delete", html);

        Assert.Contains("href=\"/admin/repositories\"", await _admin.GetPageAsync("admin/repositories"));
    }

    [Fact]
    public async Task TheListFiltersByNameFormatAndKindLikeTheRestList()
    {
        await CreateAsync("alpha");
        await CreateAsync("beta");
        Assert.NotNull(await FindAsync("beta"));

        var byName = await _admin.GetPageAsync("admin/repositories?q=ALP");
        Assert.Contains("data-repository=\"alpha\"", byName);
        Assert.DoesNotContain("data-repository=\"beta\"", byName);
        Assert.DoesNotContain("data-repository=\"default\"", byName);

        var byFormat = await _admin.GetPageAsync("admin/repositories?format=npm");
        Assert.DoesNotContain("data-repository=\"alpha\"", byFormat);
        Assert.DoesNotContain("data-repository=\"default\"", byFormat);

        var byKind = await _admin.GetPageAsync("admin/repositories?kind=Local&format=NuGet");
        Assert.Contains("data-repository=\"alpha\"", byKind);
        Assert.Contains("data-repository=\"beta\"", byKind);
        Assert.Contains("data-repository=\"default\"", byKind);

        var remote = await _admin.GetPageAsync("admin/repositories?kind=remote");
        Assert.DoesNotContain("data-repository=\"alpha\"", remote);

        // The filter form is on the page and keeps what was typed.
        Assert.Contains("id=\"repositories-filter\"", byName);
        Assert.Contains("value=\"ALP\"", byName);
    }

    [Fact]
    public async Task CreatingARepositoryListsItWithAPerRepositoryFeedAddress()
    {
        using var created = await CreateAsync("second");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var html = await _admin.GetPageAsync("admin/repositories");
        Assert.Contains("data-repository=\"second\"", html);
        Assert.Contains("/r/second/v3/index.json", html);
        Assert.Contains("Repository second was created.", html);

        var summary = await FindAsync("second");
        Assert.NotNull(summary);
        Assert.Equal(RepositoryFormat.NuGet, summary!.Format);
        Assert.Equal(RepositoryKind.Local, summary.Kind);
    }

    [Theory]
    [InlineData("", "required")]
    [InlineData("Second", "lowercase")]
    [InlineData("has space", "lowercase")]
    [InlineData("a..b", "contain")]
    [InlineData("admin", "reserved")]
    [InlineData("v3", "reserved")]
    [InlineData("r", "reserved")]
    public async Task BadOrReservedNamesAreRefusedWithAMessage(string name, string expected)
    {
        using var response = await CreateAsync(name);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"repositories-error\"", html);
        Assert.Contains(expected, html);
        Assert.Single(await ListNamesAsync());
    }

    [Fact]
    public async Task ADuplicateNameAndTheDefaultNameAreRefused()
    {
        (await CreateAsync("second")).Dispose();

        foreach (var name in new[] { "second", "default" })
        {
            using var response = await CreateAsync(name);
            var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("already exists", html);
        }

        Assert.Equal(new[] { "default", "second" }, await ListNamesAsync());
    }

    [Fact]
    public async Task EditingSavesAnonymousReadAndShowsTheFeed()
    {
        (await CreateAsync("second")).Dispose();
        var page = await _admin.GetPageAsync("admin/repositories/second");
        Assert.Contains("/r/second/v3/index.json", page);
        Assert.True((await FindAsync("second"))!.AnonymousRead);

        using var saved = await _admin.PostAsync("admin/repositories/second", new Dictionary<string, string> { { "anonymousRead", "false" } });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.False((await FindAsync("second"))!.AnonymousRead);

        using var again = await _admin.PostAsync("admin/repositories/second", new Dictionary<string, string> { { "anonymousRead", "true" } });
        Assert.True((await FindAsync("second"))!.AnonymousRead);

        using var missing = await _admin.GetAsync("admin/repositories/nope");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task AnEmptyRepositoryIsDeletedAndItsNameCanBeReused()
    {
        (await CreateAsync("second")).Dispose();

        using var deleted = await _admin.PostAsync("admin/repositories/second/delete", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);

        Assert.Null(await FindAsync("second"));
        Assert.Contains("Repository second was deleted.", await _admin.GetPageAsync("admin/repositories"));

        using var recreated = await CreateAsync("second");
        Assert.Equal(HttpStatusCode.Redirect, recreated.StatusCode);
    }

    [Fact]
    public async Task TheDefaultRepositoryCannotBeDeletedAndKeepsItsData()
    {
        var page = await _admin.GetPageAsync("admin/repositories/default/delete");
        Assert.Contains("id=\"delete-repository-default\"", page);
        Assert.DoesNotContain("btn-danger", page);

        using var response = await _admin.PostAsync(
            "admin/repositories/default/delete",
            new Dictionary<string, string>(),
            tokenPage: "admin/repositories");
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("cannot be deleted", html);
        Assert.NotNull(await FindAsync("default"));
    }

    [Fact]
    public async Task ARepositoryWithPackagesCannotBeDeleted()
    {
        (await CreateAsync("second")).Dispose();
        await SeedPackageAsync("second", "Some.Package");

        var page = await _admin.GetPageAsync("admin/repositories/second/delete");
        Assert.Contains("id=\"delete-repository-not-empty\"", page);
        Assert.DoesNotContain("btn-danger", page);

        using var response = await _admin.PostAsync(
            "admin/repositories/second/delete",
            new Dictionary<string, string>(),
            tokenPage: "admin/repositories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("still holds packages", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var summary = await FindAsync("second");
        Assert.NotNull(summary);
        Assert.Equal(1, summary!.PackageCount);
    }

    [Fact]
    public async Task ARepositoryThatOnlyKeepsHistoryRowsIsDeletedAndTheRowsArePurged()
    {
        (await CreateAsync("second")).Dispose();
        (await CreateAsync("third")).Dispose();

        // What a pushed-then-deleted package leaves behind: its catalog entries and owner row (and a report).
        foreach (var name in new[] { "second", "third" })
        {
            using var scope = _app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var info = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(name, CancellationToken.None);
            scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use(info!);
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            context.CatalogLeafs!.Add(new CatalogEntry
            {
                CommitId = Guid.NewGuid().ToString("D"),
                CommitTimestamp = DateTime.UtcNow,
                Type = CatalogEntry.PackageDeleteType,
                PackageId = "Gone.Package",
                PackageVersion = "1.0.0",
            });
            context.PackageOwners!.Add(new PackageOwner { PackageId = "Gone.Package", OwnerName = "root" });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        using var deleted = await _admin.PostAsync("admin/repositories/second/delete", new Dictionary<string, string>(), tokenPage: "admin/repositories");
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Null(await FindAsync("second"));

        using var check = _app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var db = check.ServiceProvider.GetRequiredService<IContext>();
        // Only the deleted repository's rows are purged; the other repository's remain.
        Assert.Equal(1, await db.CatalogLeafs!.IgnoreQueryFilters().CountAsync(e => e.PackageId == "Gone.Package", TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.PackageOwners!.IgnoreQueryFilters().CountAsync(e => e.PackageId == "Gone.Package", TestContext.Current.CancellationToken));
        Assert.NotNull(await FindAsync("third"));
    }

    [Fact]
    public async Task ARepositoryWithASubmissionAwaitingValidationCannotBeDeleted()
    {
        (await CreateAsync("second")).Dispose();
        using (var scope = _app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            var info = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync("second", CancellationToken.None);
            scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use(info!);
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            context.PackageValidations!.Add(new PackageValidation
            {
                SubmissionId = Guid.NewGuid().ToString("N"),
                PackageId = "Pending.Package",
                PackageVersion = "1.0.0",
                Type = PackageValidation.IndexType,
                State = PackageValidationState.Pending,
                Created = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        using var response = await _admin.PostAsync("admin/repositories/second/delete", new Dictionary<string, string>(), tokenPage: "admin/repositories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("awaiting validation", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(await FindAsync("second"));

        using var check = _app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<IContext>().PackageValidations!.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletingARepositoryRemovesEverythingItKeptInStorageAndNothingElse()
    {
        (await CreateAsync("second")).Dispose();
        (await CreateAsync("second2")).Dispose();
        var paths = new[]
        {
            "repos/second/symbols/foo.pdb/abcdefabcdefabcdefabcdefabcdefabffffffff/foo.pdb",
            "repos/second/catalog/x.json",
            "repos/second/validation/y.nupkg",
            "repos/second2/symbols/foo.pdb/abcdefabcdefabcdefabcdefabcdefabffffffff/foo.pdb",
            "symbols/foo.pdb/abcdefabcdefabcdefabcdefabcdefabffffffff/foo.pdb",
        };

        using (var scope = _app.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
            foreach (var path in paths)
            {
                using var content = new System.IO.MemoryStream(new byte[] { 1, 2, 3 });
                await storage.PutAsync(path, content, "binary/octet-stream", CancellationToken.None);
            }
        }

        using var deleted = await _admin.PostAsync("admin/repositories/second/delete", new Dictionary<string, string>(), tokenPage: "admin/repositories");
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);

        using var after = _app.Services.CreateScope();
        var listable = (IListableStorageService)after.ServiceProvider.GetRequiredService<IStorageService>();
        Assert.Empty(await listable.ListAsync("repos/second/", CancellationToken.None));
        Assert.Single(await listable.ListAsync("repos/second2/", CancellationToken.None));
        Assert.Single(await listable.ListAsync("symbols/", CancellationToken.None));
    }

    [Fact]
    public async Task PostsWithoutAnAntiforgeryTokenAreRefused()
    {
        using var response = await _admin.RawPostAsync("admin/repositories", new Dictionary<string, string> { { "name", "second" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await FindAsync("second"));
    }

    [Fact]
    public async Task WritesAreRefusedWith503WhileTheGateIsClosed()
    {
        (await CreateAsync("second")).Dispose();
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("Copying the database");

        try
        {
            using var create = await CreateAsync("third");
            using var edit = await _admin.PostAsync("admin/repositories/second", new Dictionary<string, string> { { "anonymousRead", "false" } });
            using var delete = await _admin.PostAsync(
                "admin/repositories/second/delete",
                new Dictionary<string, string>(),
                tokenPage: "admin/repositories");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, edit.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, delete.StatusCode);
        }
        finally
        {
            gate.Open();
        }

        Assert.Equal(new[] { "default", "second" }, await ListNamesAsync());
        Assert.True((await FindAsync("second"))!.AnonymousRead);
    }

    private async Task<string[]> ListNamesAsync()
    {
        using var scope = _app.Services.CreateScope();
        var all = await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().ListAsync(CancellationToken.None);

        return all.Select(r => r.Name).ToArray();
    }
}
