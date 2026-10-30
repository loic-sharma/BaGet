using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BaGet.Core.Tests.Repositories;

/// <summary>
/// Upgrading an existing database: every row that predates repositories ends up in the default one.
/// </summary>
public class RepositoryMigrationTests : IDisposable
{
    private const string BeforeRepositories = "20261018043324_AddAdministrators";

    private readonly string _path = Path.Combine(Path.GetTempPath(), "BaGetTests", Guid.NewGuid().ToString("N") + ".db");

    public RepositoryMigrationTests() => Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    private SqliteContext NewContext()
        => new(new DbContextOptionsBuilder<SqliteContext>().UseSqlite($"Data Source={_path}").Options);

    // Fills every NOT NULL column that has no default, so the test does not depend on the old schema's column list.
    private void InsertRow(string table, IDictionary<string, object> values)
    {
        var columns = new List<string>();
        var parameters = new List<object>();
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();

        using (var info = connection.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                var type = reader.GetString(2);
                var notNull = reader.GetInt32(3) == 1;
                var hasDefault = !reader.IsDBNull(4);
                var primaryKey = reader.GetInt32(5) > 0;
                if (values.TryGetValue(name, out var given))
                {
                    columns.Add(name);
                    parameters.Add(given);
                }
                else if (notNull && !hasDefault && !primaryKey)
                {
                    columns.Add(name);
                    parameters.Add(type.Contains("INT") ? 0 : "2020-01-01 00:00:00");
                }
            }
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO \"{table}\" ({string.Join(", ", columns.Select(c => "\"" + c + "\""))}) VALUES ({string.Join(", ", columns.Select((_, i) => "$p" + i))})";
        for (var i = 0; i < parameters.Count; i++) insert.Parameters.AddWithValue("$p" + i, parameters[i]);
        insert.ExecuteNonQuery();
    }

    [Fact]
    public async Task EveryExistingRowEndsUpInTheDefaultRepository()
    {
        var token = TestContext.Current.CancellationToken;
        using (var context = NewContext())
        {
            context.GetService<IMigrator>().Migrate(BeforeRepositories);
        }

        InsertRow("Packages", new Dictionary<string, object> { ["Id"] = "Old.Package", ["Version"] = "1.0.0" });
        InsertRow("PackageOwners", new Dictionary<string, object> { ["PackageId"] = "Old.Package", ["OwnerName"] = "alice" });
        InsertRow("CatalogLeafs", new Dictionary<string, object> { ["CommitId"] = Guid.NewGuid().ToString("D"), ["Type"] = "PackageDetails", ["PackageId"] = "Old.Package", ["PackageVersion"] = "1.0.0" });
        InsertRow("AbuseReports", new Dictionary<string, object> { ["PackageId"] = "Old.Package", ["PackageVersion"] = "1.0.0" });
        InsertRow("PackageValidations", new Dictionary<string, object> { ["SubmissionId"] = Guid.NewGuid().ToString("D"), ["PackageId"] = "Old.Package", ["PackageVersion"] = "1.0.0" });

        using (var context = NewContext())
        {
            await context.RunMigrationsAsync(token);

            Assert.Equal(Repository.DefaultKey, (await context.Packages!.IgnoreQueryFilters().SingleAsync(token)).RepositoryKey);
            Assert.Equal(Repository.DefaultKey, (await context.PackageOwners!.IgnoreQueryFilters().SingleAsync(token)).RepositoryKey);
            Assert.Equal(Repository.DefaultKey, (await context.CatalogLeafs!.IgnoreQueryFilters().SingleAsync(token)).RepositoryKey);
            Assert.Equal(Repository.DefaultKey, (await context.AbuseReports!.IgnoreQueryFilters().SingleAsync(token)).RepositoryKey);
            Assert.Equal(Repository.DefaultKey, (await context.PackageValidations!.IgnoreQueryFilters().SingleAsync(token)).RepositoryKey);

            // The migration itself creates the default repository row (so an operator who applies migrations
            // without RunMigrationsAtStartup still has it); EnsureDefaultAsync is then a harmless no-op.
            await new RepositoryService(context).EnsureDefaultAsync(token);
            await new RepositoryService(context).EnsureDefaultAsync(token);
            var repository = await context.Repositories!.SingleAsync(token);
            Assert.Equal(Repository.DefaultKey, repository.Key);
            Assert.Equal(Repository.DefaultName, repository.Name);

            // The default repository's scope serves the existing rows.
            Assert.Equal("Old.Package", (await context.Packages!.SingleAsync(token)).Id);
        }
    }

    [Fact]
    public async Task CreatingARepositoryNeverGivesItTheDefaultKeyEvenIfTheDefaultRowIsMissing()
    {
        var token = TestContext.Current.CancellationToken;
        using var context = NewContext();
        await context.RunMigrationsAsync(token);

        // An older or hand-built database whose Repositories table is empty.
        context.Repositories!.RemoveRange(await context.Repositories!.ToListAsync(token));
        await context.SaveChangesAsync(token);

        var created = await new RepositoryService(context).CreateAsync("second", RepositoryFormat.NuGet, RepositoryKind.Local, token);

        Assert.NotEqual(Repository.DefaultKey, created.Key);
        var rows = await context.Repositories!.AsNoTracking().OrderBy(r => r.Key).ToListAsync(token);
        Assert.Equal(new[] { Repository.DefaultName, "second" }, rows.Select(r => r.Name));
        Assert.Equal(Repository.DefaultKey, rows[0].Key);
    }

    [Fact]
    public async Task CreatingARepositoryIsRefusedWhenRowsExistButNotTheDefault()
    {
        var token = TestContext.Current.CancellationToken;
        using var context = NewContext();
        await context.RunMigrationsAsync(token);
        context.Repositories!.RemoveRange(await context.Repositories!.ToListAsync(token));
        context.Repositories!.Add(new Repository { Key = 7, Name = "other", Format = RepositoryFormat.NuGet, Kind = RepositoryKind.Local, Created = DateTime.UtcNow });
        await context.SaveChangesAsync(token);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new RepositoryService(context).CreateAsync("second", RepositoryFormat.NuGet, RepositoryKind.Local, token));
    }

    [Theory]
    [InlineData("AzureSearch", false)]
    [InlineData("azuresearch", false)]
    [InlineData("Database", true)]
    [InlineData(null, true)]
    public async Task OnlyTheDefaultRepositoryExistsWhenTheSearchIndexIsNotRepositoryAware(string? searchType, bool multiple)
    {
        var token = TestContext.Current.CancellationToken;
        using var context = NewContext();
        await context.RunMigrationsAsync(token);
        var options = Microsoft.Extensions.Options.Options.Create(new SearchOptions { Type = searchType });
        var service = new RepositoryService(context, options);
        context.Repositories!.Add(new Repository { Name = "second", Format = RepositoryFormat.NuGet, Kind = RepositoryKind.Local, Created = DateTime.UtcNow });
        await context.SaveChangesAsync(token);

        Assert.Equal(multiple, service.SupportsMultipleRepositories);
        Assert.Equal(multiple, await service.FindAsync("second", token) != null);
        Assert.NotNull(await service.FindAsync(Repository.DefaultName, token));
        Assert.Equal(multiple ? 2 : 1, (await service.ListAsync(token)).Count);
        if (!multiple)
        {
            await Assert.ThrowsAsync<NotSupportedException>(
                () => service.CreateAsync("third", RepositoryFormat.NuGet, RepositoryKind.Local, token));
        }
    }

    [Fact]
    public async Task TheSameIdAndVersionIsAllowedInAnotherRepositoryAfterTheUpgrade()
    {
        var token = TestContext.Current.CancellationToken;
        using (var context = NewContext())
        {
            await context.RunMigrationsAsync(token);
        }

        InsertRow("Repositories", new Dictionary<string, object> { ["Key"] = 2, ["Name"] = "second", ["Format"] = "NuGet", ["Kind"] = "Local", ["AnonymousRead"] = 1, ["Created"] = "2020-01-01 00:00:00" });
        InsertRow("Packages", new Dictionary<string, object> { ["Id"] = "P", ["Version"] = "1.0.0", ["RepositoryKey"] = 1 });
        InsertRow("Packages", new Dictionary<string, object> { ["Id"] = "P", ["Version"] = "1.0.0", ["RepositoryKey"] = 2 });

        // Ids are case insensitive within a repository.
        Assert.Throws<SqliteException>(() =>
            InsertRow("Packages", new Dictionary<string, object> { ["Id"] = "p", ["Version"] = "1.0.0", ["RepositoryKey"] = 2 }));

        using (var context = NewContext())
        {
            Assert.Equal(2, await context.Packages!.IgnoreQueryFilters().CountAsync(token));
        }
    }
}
