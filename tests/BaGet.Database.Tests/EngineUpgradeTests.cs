using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Database.Tests;

/// <summary>
/// The upgrade from a database that predates repositories (B7), on every engine (Testcontainers; skipped without
/// Docker): the migrations run over real rows, every row lands in the default repository with its data intact, and the
/// widened unique index then allows the same ID and version in a second repository but still refuses it in one.
/// </summary>
public class EngineUpgradeTests
{
    public static IEnumerable<object[]> EngineTypes() => Engines.Types.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(EngineTypes))]
    public async Task APreRepositoryDatabaseUpgradesIntoTheDefaultRepository(string type)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var engine = await Engines.GetOrSkipAsync(type);
        var connection = engine.NewConnectionString();

        await PreRepositoryDatabase.CreateAsync(engine.Factory, connection, engine.Options, cancellationToken);

        await using var context = (DbContext)engine.Factory.Create(connection, engine.Options);
        var iContext = (IContext)context;
        Assert.Contains(PreRepositoryDatabase.LastPreRepositoryMigration(context), await context.Database.GetAppliedMigrationsAsync(cancellationToken));
        Assert.Contains(await context.Database.GetPendingMigrationsAsync(cancellationToken), m => m.EndsWith("_AddRepositories"));

        await iContext.RunMigrationsAsync(cancellationToken);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(cancellationToken));

        // The migration assigns rows to key 1 and creates that repository itself (no startup step is needed), and the
        // key sequence continues after it on every engine.
        var repository = await iContext.Repositories!.SingleAsync(cancellationToken);
        Assert.Equal((Repository.DefaultKey, Repository.DefaultName), (repository.Key, repository.Name));
        var created = await new RepositoryService(iContext).CreateAsync("second", RepositoryFormat.NuGet, RepositoryKind.Local, cancellationToken);
        Assert.Equal(Repository.DefaultKey + 1, created.Key);

        // AddAccessControl seeds the upgrade defaults with explicit keys; generated keys continue after them on every engine.
        Assert.Equal(new[] { "Authenticated", "Everyone" }, await iContext.Groups!.Select(g => g.Name!).OrderBy(n => n).ToListAsync(cancellationToken));
        Assert.Equal(2, await iContext.PermissionTargets!.CountAsync(cancellationToken));
        Assert.Equal(new[] { AccessAction.Read, AccessAction.Annotate | AccessAction.Deploy | AccessAction.Delete | AccessAction.Manage },
            await iContext.PermissionGrants!.OrderBy(g => g.Key).Select(g => g.Actions).ToListAsync(cancellationToken));
        var extra = new Group { Name = "extra", Created = DateTime.UtcNow };
        iContext.Groups!.Add(extra);
        await iContext.SaveChangesAsync(cancellationToken);
        Assert.Equal(3, extra.Key);

        var packages = await iContext.Packages!
            .Include(p => p.Dependencies).Include(p => p.TargetFrameworks).Include(p => p.PackageTypes).Include(p => p.Deprecation)
            .OrderBy(p => p.Key).ToListAsync(cancellationToken);
        Assert.Equal(
            PreRepositoryDatabase.Packages.Select(p => (p.Id, p.Version, p.Listed)),
            packages.Select(p => (p.Id!, p.NormalizedVersionString!, p.Listed)));
        Assert.All(packages, p =>
        {
            Assert.Equal(Repository.DefaultKey, p.RepositoryKey);
            Assert.Single(p.Dependencies!);
            Assert.Single(p.TargetFrameworks!);
            Assert.Single(p.PackageTypes!);
        });
        Assert.Equal("deprecated before the upgrade", packages.Single(p => p.Id == "Other.Package").Deprecation!.Message);
        Assert.Equal(7, packages.First().Downloads);

        var owner = await iContext.PackageOwners!.SingleAsync(cancellationToken);
        Assert.Equal((Repository.DefaultKey, "Legacy.Package", PreRepositoryDatabase.UserName, true),
            (owner.RepositoryKey, owner.PackageId, owner.OwnerName, owner.IsVerified));

        var leafs = await iContext.CatalogLeafs!.ToListAsync(cancellationToken);
        Assert.Equal(2, leafs.Count);
        Assert.All(leafs, l => Assert.Equal(Repository.DefaultKey, l.RepositoryKey));
        Assert.Equal(Repository.DefaultKey, (await iContext.PackageValidations!.SingleAsync(cancellationToken)).RepositoryKey);
        Assert.Equal(Repository.DefaultKey, (await iContext.AbuseReports!.SingleAsync(cancellationToken)).RepositoryKey);
        Assert.Single(await iContext.ApiKeys!.ToListAsync(cancellationToken));

        // The same ID and version may now exist in a second repository, but not twice in one.
        var existing = packages[0];
        iContext.Packages!.Add(Copy(existing, repositoryKey: 2));
        await iContext.SaveChangesAsync(cancellationToken);

        iContext.Packages!.Add(Copy(existing, repositoryKey: Repository.DefaultKey));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => iContext.SaveChangesAsync(cancellationToken));
        Assert.True(iContext.IsUniqueConstraintViolationException(exception), exception.GetBaseException().Message);
    }

    private static Package Copy(Package source, int repositoryKey) => new()
    {
        Id = source.Id,
        Version = NuGetVersion.Parse(source.NormalizedVersionString!),
        RepositoryKey = repositoryKey,
        Listed = true,
        Authors = new[] { "author" },
        Tags = Array.Empty<string>(),
        Published = DateTime.UtcNow,
        Dependencies = new List<PackageDependency>(),
        PackageTypes = new List<PackageType>(),
        TargetFrameworks = new List<TargetFramework>(),
    };

    [Theory]
    [MemberData(nameof(EngineTypes))]
    public async Task AMissingDefaultRepositoryRowIsCreatedNotTreatedAsAnOrphan(string type)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var engine = await Engines.GetOrSkipAsync(type);
        var connection = engine.NewConnectionString();

        await OwnershipUpgradeDatabase.CreateWithoutDefaultRepositoryAsync(engine.Factory, connection, engine.Options, cancellationToken);

        await using var context = (DbContext)engine.Factory.Create(connection, engine.Options);
        await ((IContext)context).RunMigrationsAsync(cancellationToken);

        await OwnershipUpgradeDatabase.AssertDefaultRepositoryCreatedAsync((IContext)context, cancellationToken);
    }

    [Theory]
    [MemberData(nameof(EngineTypes))]
    public async Task OrphanRowsAreRemovedBeforeTheOwnershipForeignKeysAreAdded(string type)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var engine = await Engines.GetOrSkipAsync(type);
        var connection = engine.NewConnectionString();

        await OwnershipUpgradeDatabase.CreateAsync(engine.Factory, connection, engine.Options, cancellationToken);

        await using var context = (DbContext)engine.Factory.Create(connection, engine.Options);
        Assert.Contains(await context.Database.GetPendingMigrationsAsync(cancellationToken), m => m.EndsWith("_AddOwnershipForeignKeys"));
        await ((IContext)context).RunMigrationsAsync(cancellationToken);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(cancellationToken));

        await OwnershipUpgradeDatabase.AssertCleanedAsync((IContext)context, cancellationToken);
    }
}
