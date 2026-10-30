using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Database.Sqlite;
using BaGet.Database.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The foreign keys from <c>RepositoryKey</c>, <c>ProjectKey</c>, <c>UserId</c>, <c>ConfigKey</c> and the OCI tag's <c>ImageKey</c>
/// to their parents: they exist in the model (none cascades), the database enforces them, the upgrade migration removes the
/// rows that already pointed at nothing, and the code that deletes a parent still works.
/// </summary>
public class OwnershipForeignKeyTests : IDisposable
{
    private readonly BaGetApplication _app;

    public OwnershipForeignKeyTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } });
    }

    public void Dispose() => _app.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static IEnumerable<object[]> ExpectedKeys()
    {
        var repository = typeof(Repository);
        var project = typeof(Project);
        var user = typeof(BaGetUser);

        foreach (var type in new[]
                 {
                     typeof(Package), typeof(PackageOwner), typeof(CatalogEntry), typeof(PackageValidation), typeof(AbuseReport), typeof(PackageSymbol),
                     typeof(NpmPackage), typeof(MavenArtifact), typeof(OciRepository), typeof(OciBlob), typeof(ScanResult), typeof(RetentionHold),
                     typeof(ArtifactUsage), typeof(ApiKeyRepository), typeof(ReplicationConfig), typeof(RetentionPolicy), typeof(ScanPolicy),
                 })
        {
            yield return new object[] { type, "RepositoryKey", repository };
        }

        foreach (var type in new[] { typeof(Repository), typeof(PermissionTarget), typeof(RetentionPolicy), typeof(ScanPolicy) })
        {
            yield return new object[] { type, "ProjectKey", project };
        }

        foreach (var type in new[] { typeof(ApiKey), typeof(GroupMember), typeof(OrganizationMember) })
        {
            yield return new object[] { type, "UserId", user };
        }

        yield return new object[] { typeof(ReplicationRun), "ConfigKey", typeof(ReplicationConfig) };
        yield return new object[] { typeof(ReplicationItem), "ConfigKey", typeof(ReplicationConfig) };
        yield return new object[] { typeof(OciTag), "ImageKey", typeof(OciRepository) };
    }

    [Theory]
    [MemberData(nameof(ExpectedKeys))]
    public void TheColumnIsAForeignKeyThatNeverCascades(Type dependent, string property, Type principal)
    {
        using var scope = _app.Services.CreateScope();
        var model = ((DbContext)scope.ServiceProvider.GetRequiredService<IContext>()).Model;

        var keys = model.FindEntityType(dependent)!.GetForeignKeys()
            .Where(k => k.Properties.Count == 1 && k.Properties[0].Name == property && k.PrincipalEntityType.ClrType == principal)
            .ToList();

        var key = Assert.Single(keys);
        Assert.Contains(key.DeleteBehavior, new[] { DeleteBehavior.Restrict, DeleteBehavior.NoAction });
        Assert.Null(key.DependentToPrincipal);
        Assert.Null(key.PrincipalToDependent);
        var optional = dependent == typeof(RetentionPolicy) || dependent == typeof(ScanPolicy) || (property == "ProjectKey");
        Assert.Equal(!optional, key.IsRequired);
    }

    [Fact]
    public async Task TheDatabaseRefusesARowWithoutItsParent()
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        context.ApiKeys!.Add(new ApiKey { UserId = "nobody", HashedKey = new string('a', 64), Scopes = new[] { "package:push" }, Created = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        ((DbContext)context).ChangeTracker.Clear();

        context.PackageOwners!.Add(new PackageOwner { RepositoryKey = 4242, PackageId = "x", OwnerName = "y" });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        ((DbContext)context).ChangeTracker.Clear();

        context.ReplicationRuns!.Add(new ReplicationRun { ConfigKey = 4242, Started = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task AParentWithChildrenCannotBeDeletedDirectly()
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var created = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync("owned", RepositoryFormat.NuGet, RepositoryKind.Local, Token);
        context.PackageOwners!.Add(new PackageOwner { RepositoryKey = created.Key, PackageId = "x", OwnerName = "y" });
        await context.SaveChangesAsync(Token);

        ((DbContext)context).ChangeTracker.Clear();
        var row = await context.Repositories!.SingleAsync(r => r.Key == created.Key, Token);
        context.Repositories!.Remove(row);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task DeletingADockerRepositoryRemovesItsLeftoverBlobs()
    {
        using var scope = _app.Services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync("dockerhub", RepositoryFormat.Docker, RepositoryKind.Local, Token);
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var now = DateTime.UtcNow;

        // A blob left behind with no image (an image row would count as an artifact and refuse the deletion).
        context.OciBlobs!.Add(new OciBlob { RepositoryKey = created.Key, Digest = "sha256:" + new string('b', 64), Size = 1, RefCount = 0, Created = now });
        await context.SaveChangesAsync(Token);

        await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().DeleteAsync("dockerhub", Token);

        Assert.Empty(await context.OciBlobs!.IgnoreQueryFilters().ToListAsync(Token));
    }

    [Fact]
    public async Task DeletingAProjectRemovesItsRetentionPolicyAndDetachesItsRepositories()
    {
        using var scope = _app.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        var project = await access.CreateProjectAsync("shop", null, "test", Token);
        var created = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync("shopfront", RepositoryFormat.NuGet, RepositoryKind.Local, Token);
        await access.SetRepositoryProjectAsync("shopfront", "shop", "test", Token);
        context.RetentionPolicies!.Add(new RetentionPolicy { Name = "shop-policy", ProjectKey = project.Key, Created = DateTime.UtcNow, Modified = DateTime.UtcNow });
        context.ScanPolicies!.Add(new ScanPolicy { ProjectKey = project.Key, Modified = DateTime.UtcNow });
        await context.SaveChangesAsync(Token);

        await access.DeleteProjectAsync("shop", "test", Token);

        Assert.Empty(await context.RetentionPolicies!.ToListAsync(Token));
        Assert.Empty(await context.ScanPolicies!.ToListAsync(Token));
        Assert.Null((await context.Repositories!.SingleAsync(r => r.Key == created.Key, Token)).ProjectKey);
    }

    [Fact]
    public async Task ADownloadTimeForARepositoryThatNoLongerExistsIsDroppedNotRetried()
    {
        var recorder = _app.Services.GetRequiredService<ArtifactUsageRecorder>();
        recorder.Touch(4242, "left-pad", "1.0.0");
        recorder.Touch(Repository.DefaultKey, "kept", "1.0.0");

        var written = await recorder.FlushAsync(_app.Services.GetRequiredService<IServiceScopeFactory>(), Token);

        Assert.Equal(1, written);
        Assert.Equal(0, recorder.Pending);
        using var scope = _app.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<IContext>().ArtifactUsages!.IgnoreQueryFilters().ToListAsync(Token);
        Assert.Equal("kept", Assert.Single(rows).Name);
    }

    [Fact]
    public async Task AMissingDefaultRepositoryRowIsCreatedAndItsRowsAreKept()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BaGetOwnershipUpgrade", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var connection = "Data Source=" + Path.Combine(directory, "old.db");
            var options = new DatabaseOptions { Type = "Sqlite" };
            var factory = new SqliteDatabaseContextFactory();

            await OwnershipUpgradeDatabase.CreateWithoutDefaultRepositoryAsync(factory, connection, options, Token);

            await using var context = (DbContext)factory.Create(connection, options);
            await ((IContext)context).RunMigrationsAsync(Token);

            await OwnershipUpgradeDatabase.AssertDefaultRepositoryCreatedAsync((IContext)context, Token);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task AForeignKeyViolationIsNotAUniqueConstraintViolation()
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        context.PackageOwners!.Add(new PackageOwner { RepositoryKey = 4242, PackageId = "x", OwnerName = "y" });
        var foreign = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        Assert.False(context.IsUniqueConstraintViolationException(foreign));
        ((DbContext)context).ChangeTracker.Clear();

        context.Groups!.Add(new Group { Name = "dup", Created = DateTime.UtcNow });
        await context.SaveChangesAsync(Token);
        context.Groups!.Add(new Group { Name = "dup", Created = DateTime.UtcNow });
        var unique = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        Assert.True(context.IsUniqueConstraintViolationException(unique));
    }

    [Fact]
    public async Task DeletingAUserAlsoRemovesAMembershipOfNoOrganization()
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<BaGetUser>>();
        var user = new BaGetUser { UserName = "legacy", Email = "legacy@example.org" };
        Assert.True((await users.CreateAsync(user, "Passw0rd!x1")).Succeeded);
        context.OrganizationMembers!.Add(new OrganizationMember { OrganizationKey = null, UserId = user.Id, IsAdmin = false });
        await context.SaveChangesAsync(Token);

        var (_, result) = await scope.ServiceProvider.GetRequiredService<AccountDeletionService>().DeleteAsync(user, Token);

        Assert.True(result.Succeeded);
        Assert.Empty(await context.OrganizationMembers!.ToListAsync(Token));
    }

    [Fact]
    public async Task TheUpgradeMigrationDeletesOrphansAndKeepsValidRows()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BaGetOwnershipUpgrade", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var connection = "Data Source=" + Path.Combine(directory, "old.db");
            var options = new DatabaseOptions { Type = "Sqlite" };
            var factory = new SqliteDatabaseContextFactory();

            await OwnershipUpgradeDatabase.CreateAsync(factory, connection, options, Token);

            await using (var context = (DbContext)factory.Create(connection, options))
            {
                Assert.Contains(await context.Database.GetPendingMigrationsAsync(Token), m => m.EndsWith("_AddOwnershipForeignKeys"));
                await ((IContext)context).RunMigrationsAsync(Token);
                Assert.Empty(await context.Database.GetPendingMigrationsAsync(Token));

                await OwnershipUpgradeDatabase.AssertCleanedAsync((IContext)context, Token);

                // The constraints exist now: a new orphan is refused.
                var iContext = (IContext)context;
                iContext.PackageOwners!.Add(new PackageOwner { RepositoryKey = OwnershipUpgradeDatabase.MissingRepository, PackageId = "z", OwnerName = "y" });
                await Assert.ThrowsAsync<DbUpdateException>(() => iContext.SaveChangesAsync(Token));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
