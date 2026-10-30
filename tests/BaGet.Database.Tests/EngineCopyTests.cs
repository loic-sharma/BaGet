using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Database.Tests;

/// <summary>
/// <see cref="DatabaseCopyService"/> between every ordered pair of SQLite, SQL Server, PostgreSQL and MySQL, on real
/// servers (Testcontainers): per-table counts and key checksums, every key preserved (gaps included), the data
/// round-tripped, and an insert after the copy continuing past the highest copied key. Skipped when Docker is absent.
/// </summary>
public class EngineCopyTests
{
    public static IEnumerable<object[]> Pairs() => Engines.Pairs();

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task CopiesEveryTableKeepingKeysAndContinuingTheKeySequence(string sourceType, string targetType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await Engines.GetOrSkipAsync(sourceType);
        var target = await Engines.GetOrSkipAsync(targetType);

        var sourceConnection = source.NewConnectionString();
        var targetConnection = target.NewConnectionString();
        var seeded = await SeedAsync(source, sourceConnection);

        var gate = new MaintenanceGate();
        var service = CreateService(source, sourceConnection, gate);
        var request = new DatabaseCopyRequest(
            target.Type, targetConnection, target.MySqlServerVersion, new Dictionary<SettingDescriptor, object>(), "tests");

        Assert.True(service.TryQueue(request, out var refusal), refusal);
        await service.RunAsync(request, CancellationToken.None);

        var status = service.Status;
        Assert.True(status.Phase == DatabaseCopyPhase.Succeeded, string.Join(" | ", status.Errors));
        Assert.Equal(status.RowsTotal, status.RowsCopied);
        Assert.True(gate.IsClosed);

        using var sourceDb = (DbContext)source.Factory.Create(sourceConnection, source.Options);
        using var targetDb = (DbContext)target.Factory.Create(targetConnection, target.Options);

        foreach (var table in DatabaseTable.InDependencyOrder(targetDb.Model))
        {
            var expected = await table.ReadBatchAsync(sourceDb, 0, int.MaxValue, CancellationToken.None);
            var actual = await table.ReadBatchAsync(targetDb, 0, int.MaxValue, CancellationToken.None);
            Assert.True(expected.Count == actual.Count, $"{table.Name}: {expected.Count} rows in the source, {actual.Count} in the target");

            var compared = table.EntityType.GetProperties()
                .Where(p => p.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate)
                .ToList();
            for (var i = 0; i < expected.Count; i++)
            {
                foreach (var property in compared)
                {
                    Assert.Equal(
                        DatabaseTable.Format(DatabaseTable.ValueOf(property, expected[i].Entity)),
                        DatabaseTable.Format(DatabaseTable.ValueOf(property, actual[i].Entity)));
                }

                Assert.Equal(
                    expected[i].Shadow.OrderBy(p => p.Key).Select(p => p.Key + "=" + DatabaseTable.Format(p.Value)),
                    actual[i].Shadow.OrderBy(p => p.Key).Select(p => p.Key + "=" + DatabaseTable.Format(p.Value)));
            }
        }

        // The seeded data really is there, with its original (gapped) keys.
        Assert.Equal(new[] { "default", "second" }, await targetDb.Set<Repository>().OrderBy(r => r.Key).Select(r => r.Name).ToListAsync(cancellationToken));
        Assert.Equal(1, await targetDb.Set<Package>().IgnoreQueryFilters().CountAsync(p => p.RepositoryKey == 2, cancellationToken));
        Assert.Equal(seeded.PackageKeys, await targetDb.Set<Package>().IgnoreQueryFilters().OrderBy(p => p.Key).Select(p => p.Key).ToListAsync(cancellationToken));
        Assert.Equal(seeded.OrganizationKeys, await targetDb.Set<Organization>().OrderBy(o => o.Key).Select(o => o.Key).ToListAsync(cancellationToken));
        Assert.Equal(2, await targetDb.Set<PackageDependency>().CountAsync(cancellationToken));

        // A key generated after the copy continues after the highest copied one.
        var later = new Organization { Name = "Later", Created = DateTime.UtcNow };
        targetDb.Set<Organization>().Add(later);
        var laterPackage = new Package
        {
            Id = "Later.Package",
            Version = NuGetVersion.Parse("9.0.0"),
            Published = DateTime.UtcNow,
            Listed = true,
        };
        targetDb.Set<Package>().Add(laterPackage);
        await targetDb.SaveChangesAsync(cancellationToken);

        Assert.True(later.Key > seeded.OrganizationKeys.Max(), $"Organization key {later.Key}");
        Assert.True(laterPackage.Key > seeded.PackageKeys.Max(), $"Package key {laterPackage.Key}");
    }

    private static DatabaseCopyService CreateService(Engine source, string sourceConnection, MaintenanceGate gate)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => source.Factory.Create(sourceConnection, source.Options));
        var provider = services.BuildServiceProvider();

        var monitor = new Mock<IOptionsMonitor<DatabaseOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(new DatabaseOptions
        {
            Type = source.Type,
            ConnectionString = sourceConnection,
            MySqlServerVersion = source.MySqlServerVersion,
        });

        var configuration = new ConfigurationBuilder().Build();
        var settings = new SettingsService(
            configuration,
            new SettingsCatalog(Array.Empty<SettingsSchemaRegistration>()),
            Array.Empty<SettingsSchemaRegistration>(),
            provider,
            NullLogger<SettingsService>.Instance,
            new SettingsOverridesStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "baget-engines-" + Guid.NewGuid().ToString("N") + ".json")));

        return new DatabaseCopyService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            AllFactories,
            gate,
            settings,
            monitor.Object,
            NullLogger<DatabaseCopyService>.Instance);
    }

    private static IEnumerable<IDatabaseContextFactory> AllFactories => new IDatabaseContextFactory[]
    {
        new BaGet.Database.Sqlite.SqliteDatabaseContextFactory(),
        new BaGet.Database.SqlServer.SqlServerDatabaseContextFactory(),
        new BaGet.Database.PostgreSql.PostgreSqlDatabaseContextFactory(),
        new BaGet.Database.MySql.MySqlDatabaseContextFactory(),
    };

    private sealed record Seeded(List<int> PackageKeys, List<int> OrganizationKeys);

    /// <summary>
    /// Migrates a new database and fills it so that keys have gaps (the middle rows are deleted), strings are
    /// non-ASCII, dates are UTC, arrays and optional columns are both set and null, and children reference parents.
    /// </summary>
    private static async Task<Seeded> SeedAsync(Engine engine, string connectionString)
    {
        using var context = (DbContext)engine.Factory.Create(connectionString, engine.Options);
        await ((IContext)context).RunMigrationsAsync(CancellationToken.None);

        var published = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var packages = new List<Package>();
        for (var i = 1; i <= 4; i++)
        {
            var package = new Package
            {
                Id = "Seed.Package",
                Version = NuGetVersion.Parse($"{i}.0.0"),
                Authors = new[] { "Ana", "Zoë Müller", "日本語" },
                Tags = i % 2 == 0 ? Array.Empty<string>() : new[] { "a", "b" },
                Description = i == 1 ? null : "Description " + i + " - naïve café",
                Downloads = 1000L * i,
                Listed = i != 3,
                Published = published.AddDays(i),
                IsPrerelease = false,
                ProjectUrl = new Uri("https://example.org/p/" + i),
                Dependencies = new List<PackageDependency>(),
                PackageTypes = new List<PackageType> { new() { Name = "Dependency", Version = "1.0" } },
                TargetFrameworks = new List<TargetFramework> { new() { Moniker = "net10.0" } },
            };
            if (i == 2)
            {
                package.Dependencies.Add(new PackageDependency { Id = "Dep.A", VersionRange = "[1.0.0, )", TargetFramework = "net10.0" });
                package.Dependencies.Add(new PackageDependency { Id = "Dep.B", VersionRange = null, TargetFramework = null });
            }

            packages.Add(package);
        }

        // A second repository holding the same id and version as the default one.
        packages.Add(new Package
        {
            Id = "Seed.Package",
            Version = NuGetVersion.Parse("1.0.0"),
            RepositoryKey = 2,
            Published = published,
            Listed = true,
            Dependencies = new List<PackageDependency>(),
            PackageTypes = new List<PackageType> { new() { Name = "Dependency", Version = "1.0" } },
            TargetFrameworks = new List<TargetFramework>(),
        });
        // "default" (key 1) is created by the AddRepositories migration; "second" gets key 2. Saved first: the packages
        // that name key 2 have a foreign key to it, and EF cannot order the inserts by a key it has not been given yet.
        context.Set<Repository>().Add(new Repository { Name = "second", Created = published });
        await context.SaveChangesAsync();

        context.Set<Package>().AddRange(packages);
        var organizations = new List<Organization>
        {
            new() { Name = "One", Created = published },
            new() { Name = "Two", Created = published.AddHours(1) },
            new() { Name = "Three", Created = published.AddHours(2) },
        };
        context.Set<Organization>().AddRange(organizations);
        context.Set<BaGetUser>().Add(new BaGetUser { Id = "user-1", UserName = "alice", NormalizedUserName = "ALICE" });
        await context.SaveChangesAsync();

        context.Set<CatalogEntry>().Add(new CatalogEntry
        {
            CommitId = Guid.NewGuid().ToString(),
            CommitTimestamp = published,
            Type = CatalogEntry.PackageDetailsType,
            PackageId = "Seed.Package",
            PackageVersion = "1.0.0",
        });
        context.Set<OrganizationMember>().Add(new OrganizationMember { OrganizationKey = organizations[2].Key, UserId = "user-1", IsAdmin = true });
        await context.SaveChangesAsync();

        // Gaps in the keys: delete a package and an organization in the middle.
        context.Set<Package>().Remove(packages[2]);
        context.Set<Organization>().Remove(organizations[1]);
        await context.SaveChangesAsync();

        return new Seeded(
            packages.Where(p => p != packages[2]).Select(p => p.Key).OrderBy(k => k).ToList(),
            organizations.Where(o => o != organizations[1]).Select(o => o.Key).OrderBy(k => k).ToList());
    }
}
