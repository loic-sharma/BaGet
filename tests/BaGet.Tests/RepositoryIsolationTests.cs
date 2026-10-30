using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Catalog.Database;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// Repositories are isolated: the same ID and version can live in two, and a query in one never sees the other's
/// rows. The repository is chosen per DI scope through <see cref="IRepositoryContext"/>, as routing, workers and the
/// CLI do.
/// </summary>
public class RepositoryIsolationTests : IDisposable
{
    private static readonly CancellationToken Cancellation = CancellationToken.None;

    private readonly BaGetApplication _app;
    private RepositoryInfo _second = null!;

    public RepositoryIsolationTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Validation:Enabled", "true" },
            { "Accounts:Enabled", "true" },
        });
        _app.CreateClient().Dispose();
    }

    public void Dispose() => _app.Dispose();

    private async Task<RepositoryInfo> CreateSecondAsync()
    {
        using var scope = _app.Services.CreateScope();
        _second = await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync("second", RepositoryFormat.NuGet, RepositoryKind.Local, Cancellation);
        return _second;
    }

    private IServiceScope Scope(RepositoryInfo? repository = null)
        => repository == null
            ? _app.Services.CreateScope()
            : _app.Services.GetRequiredService<IServiceScopeFactory>().CreateRepositoryScope(repository);

    private static Package NewPackage(string id, string version, long downloads = 0) => new()
    {
        Id = id,
        Version = NuGetVersion.Parse(version),
        Listed = true,
        Downloads = downloads,
        Authors = new[] { "author" },
        Tags = Array.Empty<string>(),
        Description = "A test package.",
        Published = DateTime.UtcNow,
        Dependencies = new List<PackageDependency>(),
        PackageTypes = new List<PackageType> { new() { Name = "Dependency", Version = "0.0" } },
        TargetFrameworks = new List<TargetFramework>(),
    };

    [Fact]
    public async Task TheDefaultRepositoryExistsAfterStartupWithKeyOne()
    {
        using var scope = Scope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        var row = await context.Repositories!.SingleAsync(Cancellation);

        Assert.Equal(Repository.DefaultKey, row.Key);
        Assert.Equal(Repository.DefaultName, row.Name);
        Assert.Equal(RepositoryFormat.NuGet, row.Format);
        Assert.Equal(RepositoryKind.Local, row.Kind);
        Assert.Equal(RepositoryInfo.Default, scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Current);
    }

    [Fact]
    public async Task TheSameIdAndVersionCanLiveInTwoRepositories()
    {
        var second = await CreateSecondAsync();

        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
            Assert.Equal(PackageAddResult.Success, await db.AddAsync(NewPackage("Same.Package", "1.0.0"), Cancellation));
            Assert.Equal(PackageAddResult.PackageAlreadyExists, await db.AddAsync(NewPackage("Same.Package", "1.0.0"), Cancellation));
        }

        using (var scope = Scope(second))
        {
            var db = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
            Assert.Equal(PackageAddResult.Success, await db.AddAsync(NewPackage("Same.Package", "1.0.0"), Cancellation));
        }

        using (var scope = Scope())
        {
            var found = await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().FindAsync("Same.Package", true, Cancellation);
            Assert.Equal(Repository.DefaultKey, Assert.Single(found).RepositoryKey);
        }

        using (var scope = Scope(second))
        {
            var found = await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().FindAsync("Same.Package", true, Cancellation);
            Assert.Equal(second.Key, Assert.Single(found).RepositoryKey);
        }
    }

    [Fact]
    public async Task APackageInOneRepositoryIsInvisibleAndUntouchableFromTheOther()
    {
        var second = await CreateSecondAsync();
        var version = NuGetVersion.Parse("1.0.0");

        using (var scope = Scope(second))
        {
            await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().AddAsync(NewPackage("Only.Second", "1.0.0"), Cancellation);
        }

        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
            Assert.False(await db.ExistsAsync("Only.Second", Cancellation));
            Assert.Null(await db.FindOrNullAsync("Only.Second", version, true, Cancellation));
            Assert.False(await db.UnlistPackageAsync("Only.Second", version, Cancellation));
            Assert.False(await db.HardDeletePackageAsync("Only.Second", version, Cancellation));
        }

        using (var scope = Scope(second))
        {
            var db = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
            Assert.True(await db.ExistsAsync("Only.Second", version, Cancellation));
            Assert.True(await db.HardDeletePackageAsync("Only.Second", version, Cancellation));
        }
    }

    [Fact]
    public async Task DeletingInOneRepositoryLeavesTheSameVersionInTheOther()
    {
        var second = await CreateSecondAsync();
        var version = NuGetVersion.Parse("1.0.0");
        foreach (var repository in new RepositoryInfo?[] { null, second })
        {
            using var scope = Scope(repository);
            await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().AddAsync(NewPackage("Both.Package", "1.0.0"), Cancellation);
        }

        using (var scope = Scope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().HardDeletePackageAsync("Both.Package", version, Cancellation));
        }

        using (var scope = Scope(second))
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().ExistsAsync("Both.Package", version, Cancellation));
        }
    }

    [Fact]
    public async Task SearchSeesOnlyTheCurrentRepository()
    {
        var second = await CreateSecondAsync();

        using (var scope = Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().AddAsync(NewPackage("Alpha.Default", "1.0.0"), Cancellation);
        }

        using (var scope = Scope(second))
        {
            await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().AddAsync(NewPackage("Alpha.Second", "1.0.0"), Cancellation);
        }

        foreach (var (repository, expected) in new[] { ((RepositoryInfo?)null, "Alpha.Default"), (second, "Alpha.Second") })
        {
            using var scope = Scope(repository);
            var search = scope.ServiceProvider.GetRequiredService<ISearchService>();

            var autocomplete = await search.AutocompleteAsync(new AutocompleteRequest { Query = "Alpha", Take = 20 }, Cancellation);

            Assert.Equal(new[] { expected }, autocomplete.Data);
        }
    }

    [Fact]
    public async Task OwnersCatalogValidationsAndAbuseReportsAreScoped()
    {
        var second = await CreateSecondAsync();

        foreach (var repository in new RepositoryInfo?[] { null, second })
        {
            using var scope = Scope(repository);
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            var name = repository?.Name ?? "default";

            context.PackageOwners!.Add(new PackageOwner { PackageId = "Owned." + name, OwnerName = "alice" });
            context.AbuseReports!.Add(new AbuseReport { PackageId = "P", PackageVersion = "1.0.0", Reason = "Other", Details = name, Created = DateTime.UtcNow });
            context.PackageValidations!.Add(new PackageValidation
            {
                SubmissionId = Guid.NewGuid().ToString("D"),
                PackageId = "P",
                PackageVersion = "1.0.0",
                Type = "T-" + name,
                State = PackageValidationState.Succeeded,
                Created = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(Cancellation);

            await scope.ServiceProvider.GetRequiredService<ICatalogDatabase>().AddAsync(new CatalogEntry
            {
                CommitId = Guid.NewGuid().ToString("D"),
                CommitTimestamp = DateTime.UtcNow,
                Type = CatalogEntry.PackageDetailsType,
                PackageId = "P-" + name,
                PackageVersion = "1.0.0",
            }, Cancellation);
        }

        foreach (var repository in new RepositoryInfo?[] { null, second })
        {
            using var scope = Scope(repository);
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            var name = repository?.Name ?? "default";
            var key = repository?.Key ?? Repository.DefaultKey;

            Assert.Equal("Owned." + name, (await context.PackageOwners!.SingleAsync(Cancellation)).PackageId);
            Assert.Equal(name, (await context.AbuseReports!.SingleAsync(Cancellation)).Details);
            Assert.Equal("T-" + name, (await context.PackageValidations!.SingleAsync(Cancellation)).Type);

            var catalog = scope.ServiceProvider.GetRequiredService<ICatalogDatabase>();
            var entries = await catalog.GetEntriesAsync(0, 100, Cancellation);
            Assert.Equal("P-" + name, Assert.Single(entries).PackageId);
            Assert.Equal(key, entries[0].RepositoryKey);
            Assert.Single(await catalog.GetCommitTimestampsAsync(Cancellation));
            Assert.Null(await catalog.FindOrNullAsync(Guid.NewGuid().ToString("D"), Cancellation));
        }

        // Nothing is lost: with the filters off, both repositories' rows are there.
        using (var scope = Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            Assert.Equal(2, await context.PackageOwners!.IgnoreQueryFilters().CountAsync(Cancellation));
            Assert.Equal(2, await context.CatalogLeafs!.IgnoreQueryFilters().CountAsync(Cancellation));
        }
    }

    [Fact]
    public async Task ARowAddedWithoutARepositoryTakesTheCurrentOne()
    {
        var second = await CreateSecondAsync();

        using var scope = Scope(second);
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        context.AbuseReports!.Add(new AbuseReport { PackageId = "P", PackageVersion = "1.0.0", Reason = "Other", Details = "d", Created = DateTime.UtcNow });
        await context.SaveChangesAsync(Cancellation);

        Assert.Equal(second.Key, (await context.AbuseReports.SingleAsync(Cancellation)).RepositoryKey);
    }

    [Fact]
    public async Task TheCurrentRepositoryMayBeSelectedAfterTheContextWasCreated()
    {
        var second = await CreateSecondAsync();

        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositoryContext>();

        await db.AddAsync(NewPackage("Switch.Package", "1.0.0"), Cancellation);
        Assert.True(await db.ExistsAsync("Switch.Package", Cancellation));

        repository.Use(second);

        Assert.False(await db.ExistsAsync("Switch.Package", Cancellation));
    }

    [Fact]
    public async Task APackageSubmittedToARepositoryIsValidatedAndIndexedThere()
    {
        var second = await CreateSecondAsync();

        // The worker runs outside any repository; the queued submission must still land in the one it was pushed to.
        using (var scope = Scope(second))
        {
            using var package = TestPackages.Build("Validated.Second", "1.0.0");
            var result = await scope.ServiceProvider.GetRequiredService<IPackageSubmissionService>().SubmitAsync(package, Cancellation);
            Assert.Equal(PackageIndexingResult.Accepted, result);
        }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        var version = NuGetVersion.Parse("1.0.0");
        while (true)
        {
            using var scope = Scope(second);
            if (await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().ExistsAsync("Validated.Second", version, Cancellation)) break;
            Assert.True(DateTime.UtcNow < deadline, "The package was not indexed in time.");
            await Task.Delay(100, Cancellation);
        }

        using (var scope = Scope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().ExistsAsync("Validated.Second", version, Cancellation));
        }
    }

    [Fact]
    public async Task ARepositoryNameIsUniqueAndValidated()
    {
        await CreateSecondAsync();

        using var scope = Scope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();

        await Assert.ThrowsAsync<RepositoryExistsException>(
            () => repositories.CreateAsync("second", RepositoryFormat.NuGet, RepositoryKind.Local, Cancellation));
        await Assert.ThrowsAsync<RepositoryExistsException>(
            () => repositories.CreateAsync(Repository.DefaultName, RepositoryFormat.NuGet, RepositoryKind.Local, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(
            () => repositories.CreateAsync("Not Valid", RepositoryFormat.NuGet, RepositoryKind.Local, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(
            () => repositories.CreateAsync("api", RepositoryFormat.NuGet, RepositoryKind.Local, Cancellation));
        // A remote repository needs its upstream; virtual ones do not exist yet.
        await Assert.ThrowsAsync<ArgumentException>(
            () => repositories.CreateAsync("remote", RepositoryFormat.NuGet, RepositoryKind.Remote, Cancellation));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => repositories.CreateAsync("virtual", RepositoryFormat.NuGet, RepositoryKind.Virtual, Cancellation));

        Assert.Equal(new[] { "default", "second" }, (await repositories.ListAsync(Cancellation)).Select(r => r.Name));
        Assert.Equal(_second, await repositories.FindAsync("second", Cancellation));
        Assert.Equal(_second, await repositories.FindAsync(_second.Key, Cancellation));
        Assert.Null(await repositories.FindAsync("missing", Cancellation));
    }

    [Fact]
    public async Task DeletingAnAccountRemovesItsOwnershipInEveryRepository()
    {
        var second = await CreateSecondAsync();

        string userId;
        using (var scope = Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var alice = new BaGetUser { UserName = "alice", Created = DateTime.UtcNow };
            Assert.True((await users.CreateAsync(alice, "Passw0rd!")).Succeeded);
            userId = alice.Id;
        }

        foreach (var repository in new RepositoryInfo?[] { null, second })
        {
            using var scope = Scope(repository);
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            context.PackageOwners!.Add(new PackageOwner { PackageId = "Shared", OwnerName = "alice" });
            context.PackageOwners.Add(new PackageOwner { PackageId = "Shared", OwnerName = "bob" });
            context.PackageOwners.Add(new PackageOwner { PackageId = "Sole." + (repository?.Name ?? "default"), OwnerName = "alice" });
            await context.SaveChangesAsync(Cancellation);
        }

        using (var scope = Scope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>().FindByIdAsync(userId);
            var deletion = scope.ServiceProvider.GetRequiredService<AccountDeletionService>();

            // Sole ownership in either repository blocks the self-service deletion ...
            var blockers = await deletion.GetBlockersAsync(user, Cancellation);
            Assert.Equal(new[] { "Sole.default", "Sole.second" }, blockers.LastOwnerPackageIds.OrderBy(i => i));
            Assert.Equal(new[] { "default", "second" }, blockers.LastOwnerPackages.Select(p => p.RepositoryName).OrderBy(n => n));

            // ... and an administrator's deletion leaves nobody's ownership behind anywhere.
            var result = await deletion.DeleteByAdministratorAsync(user, deleteSoleOwnedPackages: false, "admin", Cancellation);
            Assert.True(result.Result.Succeeded);
        }

        using (var scope = Scope(second))
        {
            var owners = await scope.ServiceProvider.GetRequiredService<IContext>().PackageOwners!
                .IgnoreQueryFilters().Select(o => o.OwnerName).Distinct().ToListAsync(Cancellation);
            Assert.Equal(new[] { "bob" }, owners);
        }
    }
}
