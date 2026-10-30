using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Database.MySql;
using BaGet.Database.PostgreSql;
using BaGet.Database.SqlServer;
using BaGet.Database.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// <see cref="IDatabaseContextFactory"/> per relational provider, and the explicit-key hooks
/// (<see cref="IContext.BeginKeyedInsertAsync"/> / <see cref="IContext.EndKeyedInsertAsync"/>).
/// Only SQLite can run here; the other engines are checked offline by the SQL their hooks issue.
/// </summary>
public class DatabaseContextFactoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "baget-factory-" + Guid.NewGuid().ToString("N"));

    public DatabaseContextFactoryTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static IEntityType Table<TContext>(Func<string, DatabaseOptions?, Action<DbContextOptionsBuilder>?, IContext> create, Type entity)
        where TContext : DbContext
    {
        using var context = (DbContext)create("Server=127.0.0.1;Database=x;User=u", new DatabaseOptions { MySqlServerVersion = "8.0.0-mysql" }, null);
        return context.Model.FindEntityType(entity)!;
    }

    [Fact]
    public void EachRelationalProviderRegistersOneFactoryByType()
    {
        var services = new ServiceCollection();
        var app = new BaGet.Core.BaGetApplication(services);

        app.AddSqliteDatabase();
        app.AddSqlServerDatabase();
        app.AddPostgreSqlDatabase();
        app.AddMySqlDatabase();
        app.AddSqliteDatabase(); // registering twice must not duplicate

        using var provider = services.BuildServiceProvider();
        var types = provider.GetServices<IDatabaseContextFactory>().Select(f => f.DatabaseType).OrderBy(t => t).ToArray();

        Assert.Equal(new[] { "MySql", "PostgreSql", "Sqlite", "SqlServer" }, types);
    }

    [Fact]
    public void ConfigureCallbackCustomisesTheOptionsBuilder()
    {
        var called = 0;
        using var context = (DbContext)new SqliteDatabaseContextFactory().Create(
            "Data Source=" + Path.Combine(_directory, "a.db"),
            configure: b => { called++; b.EnableSensitiveDataLogging(); });

        Assert.Equal(1, called);
        Assert.True(context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()!.IsSensitiveDataLoggingEnabled);
    }

    [Fact]
    public void MySqlHonoursTheConfiguredServerVersionWithoutConnecting()
    {
        // AutoDetect against this unreachable server would throw; the configured version avoids it.
        using var context = (DbContext)new MySqlDatabaseContextFactory().Create(
            "Server=127.0.0.1;Port=1;Database=x;User=u",
            new DatabaseOptions { MySqlServerVersion = "10.11.0-mariadb" });

        Assert.Equal("Microting.EntityFrameworkCore.MySql", context.Database.ProviderName);
    }

    [Fact]
    public async Task SqliteFactoryMigratesAndContinuesKeysAfterExplicitInserts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(_directory, "target.db");
        var factory = new SqliteDatabaseContextFactory();

        await using (var context = (DbContext)factory.Create("Data Source=" + path))
        {
            var iContext = (IContext)context;
            await iContext.RunMigrationsAsync(CancellationToken.None);

            var table = context.Model.FindEntityType(typeof(Organization))!;
            await iContext.BeginKeyedInsertAsync(table, CancellationToken.None);
            iContext.Organizations!.Add(new Organization { Key = 5, Name = "five" });
            iContext.Organizations!.Add(new Organization { Key = 9, Name = "nine" });
            await iContext.SaveChangesAsync(CancellationToken.None);
            await iContext.EndKeyedInsertAsync(table, CancellationToken.None);

            var next = new Organization { Name = "next" };
            iContext.Organizations!.Add(next);
            await iContext.SaveChangesAsync(CancellationToken.None);

            Assert.Equal(10, next.Key);
        }

        // A second context on the same file sees the data: the factory targets the given connection string.
        await using var again = (DbContext)factory.Create("Data Source=" + path);
        Assert.Equal(3, await ((IContext)again).Organizations!.CountAsync(cancellationToken));
    }

    [Fact]
    public void SqlServerHookTurnsIdentityInsertOnAndOffForIdentityTablesOnly()
    {
        var packages = Table<SqlServerContext>(new SqlServerDatabaseContextFactory().Create, typeof(Package));
        var users = Table<SqlServerContext>(new SqlServerDatabaseContextFactory().Create, typeof(BaGetUser));

        Assert.Equal("SET IDENTITY_INSERT [Packages] ON", SqlServerContext.BuildIdentityInsertSql(packages, on: true));
        Assert.Equal("SET IDENTITY_INSERT [Packages] OFF", SqlServerContext.BuildIdentityInsertSql(packages, on: false));
        Assert.Null(SqlServerContext.BuildIdentityInsertSql(users, on: true)); // string key, no identity
    }

    [Fact]
    public void PostgreSqlHookResetsTheSequenceOnlyForSerialKeys()
    {
        var packages = Table<PostgreSqlContext>(new PostgreSqlDatabaseContextFactory().Create, typeof(Package));
        var users = Table<PostgreSqlContext>(new PostgreSqlDatabaseContextFactory().Create, typeof(BaGetUser));

        var sql = Assert.Single(PostgreSqlContext.BuildSetValSql(packages));
        Assert.Equal(
            "SELECT setval(pg_get_serial_sequence('\"Packages\"', 'Key'), COALESCE(MAX(\"Key\"), 1), MAX(\"Key\") IS NOT NULL) FROM \"Packages\"",
            sql);
        Assert.Empty(PostgreSqlContext.BuildSetValSql(users));
    }

    [Fact]
    public async Task SqliteAndMySqlHooksDoNothing()
    {
        var mysql = Table<MySqlContext>(new MySqlDatabaseContextFactory().Create, typeof(Package));

        // MySQL has no server here: a hook that touched the database would throw.
        using var context = (DbContext)new MySqlDatabaseContextFactory().Create(
            "Server=127.0.0.1;Port=1;Database=x;User=u", new DatabaseOptions { MySqlServerVersion = "8.0.0-mysql" });
        var iContext = (IContext)context;
        await iContext.BeginKeyedInsertAsync(mysql, CancellationToken.None);
        await iContext.EndKeyedInsertAsync(mysql, CancellationToken.None);

        // SQLite: the no-op needs no connection either, and the next key still follows the max
        // (covered end to end in SqliteFactoryMigratesAndContinuesKeysAfterExplicitInserts).
        using var sqlite = (DbContext)new SqliteDatabaseContextFactory().Create("Data Source=" + Path.Combine(_directory, "n.db"));
        var sqliteTable = sqlite.Model.FindEntityType(typeof(Package))!;
        await ((IContext)sqlite).BeginKeyedInsertAsync(sqliteTable, CancellationToken.None);
        await ((IContext)sqlite).EndKeyedInsertAsync(sqliteTable, CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(_directory, "n.db")));
    }
}
