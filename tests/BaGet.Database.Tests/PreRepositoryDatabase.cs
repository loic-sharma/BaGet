using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Database.Tests;

/// <summary>
/// A database as an install from before repositories existed would have it: schema migrated only up to
/// <see cref="LastPreRepositoryMigration(DbContext)"/> and filled with raw SQL (the current model has columns that database
/// lacks), on any engine. Linked into <c>BaGet.Tests</c> too, which starts the real application on the result.
/// </summary>
public static class PreRepositoryDatabase
{
    /// <summary>
    /// The id of the last migration before <c>AddRepositories</c> in the context's provider (each provider has its
    /// own timestamps).
    /// </summary>
    public static string LastPreRepositoryMigration(DbContext context)
    {
        var ids = context.GetService<IMigrationsAssembly>().Migrations.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var index = ids.FindIndex(id => id.EndsWith("_AddRepositories", StringComparison.Ordinal));
        if (index < 1) throw new InvalidOperationException("The AddRepositories migration was not found.");

        return ids[index - 1];
    }

    /// <summary>The plaintext of the seeded API key (scope <c>package:push</c>, owned by <see cref="UserName"/>).</summary>
    public const string ApiKey = "bgt_pre-upgrade-key";

    public const string UserName = "alice";

    /// <summary>Packages seeded, as (id, version, listed).</summary>
    public static readonly (string Id, string Version, bool Listed)[] Packages =
    {
        ("Legacy.Package", "1.0.0", true),
        ("Legacy.Package", "2.0.0", false),
        ("Other.Package", "1.0.0", true),
    };

    /// <summary>Creates the database at <paramref name="connectionString"/> in its pre-repository state and fills it.</summary>
    public static async Task CreateAsync(
        IDatabaseContextFactory factory,
        string connectionString,
        DatabaseOptions options,
        CancellationToken cancellationToken)
    {
        await using var context = (DbContext)factory.Create(connectionString, options);
        context.GetService<IMigrator>().Migrate(LastPreRepositoryMigration(context));
        await SeedAsync(context, options.Type ?? string.Empty, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedAsync(DbContext context, string engine, CancellationToken token)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(token).ConfigureAwait(false);

        var created = new DateTime(2019, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var seed = new Inserter(connection, engine);

        await seed.InsertAsync("AspNetUsers", new()
        {
            ["Id"] = "user-alice", ["Created"] = created, ["UserName"] = UserName, ["NormalizedUserName"] = "ALICE",
            ["EmailConfirmed"] = false, ["PhoneNumberConfirmed"] = false, ["TwoFactorEnabled"] = false,
            ["LockoutEnabled"] = true, ["AccessFailedCount"] = 0, ["IsAdministrator"] = false, ["SecurityStamp"] = "stamp",
        }, token).ConfigureAwait(false);

        await seed.InsertAsync("ApiKeys", new()
        {
            ["UserId"] = "user-alice", ["Description"] = "pre-upgrade", ["HashedKey"] = Hash(ApiKey),
            ["Scopes"] = "[\"package:push\"]", ["PackageGlobPattern"] = "*", ["Created"] = created,
        }, token).ConfigureAwait(false);

        foreach (var (id, version, listed) in Packages)
        {
            await seed.InsertAsync("Packages", new()
            {
                ["Id"] = id, ["Version"] = version, ["Authors"] = "[\"Legacy author\"]", ["Description"] = "Seeded before repositories.",
                ["Downloads"] = id == "Legacy.Package" ? 7L : 0L, ["HasReadme"] = false, ["Listed"] = listed, ["Published"] = created,
                ["RequireLicenseAcceptance"] = false, ["Tags"] = "[]", ["IsPrerelease"] = false, ["SemVerLevel"] = 0,
                ["OriginalVersion"] = version, ["HasEmbeddedIcon"] = false,
            }, token).ConfigureAwait(false);

            var key = await seed.PackageKeyAsync(id, version, token).ConfigureAwait(false);
            await seed.InsertAsync("PackageTypes", new() { ["Name"] = "Dependency", ["Version"] = "0.0", ["PackageKey"] = key }, token).ConfigureAwait(false);
            await seed.InsertAsync("TargetFrameworks", new() { ["Moniker"] = "net8.0", ["PackageKey"] = key }, token).ConfigureAwait(false);
            await seed.InsertAsync("PackageDependencies", new()
            {
                ["Id"] = "Some.Dependency", ["VersionRange"] = "[1.0.0, )", ["TargetFramework"] = "net8.0", ["PackageKey"] = key,
            }, token).ConfigureAwait(false);

            if (id == "Other.Package")
            {
                await seed.InsertAsync("PackageDeprecations", new()
                {
                    ["Reasons"] = "[\"Legacy\"]", ["Message"] = "deprecated before the upgrade", ["PackageKey"] = key,
                }, token).ConfigureAwait(false);
            }
        }

        await seed.InsertAsync("PackageOwners", new() { ["PackageId"] = "Legacy.Package", ["OwnerName"] = UserName, ["IsVerified"] = true }, token).ConfigureAwait(false);

        for (var i = 0; i < 2; i++)
        {
            await seed.InsertAsync("CatalogLeafs", new()
            {
                ["CommitId"] = Guid.NewGuid().ToString("D"), ["CommitTimestamp"] = created.AddMinutes(i), ["Type"] = "nuget:PackageDetails",
                ["PackageId"] = "Legacy.Package", ["PackageVersion"] = i == 0 ? "1.0.0" : "2.0.0",
            }, token).ConfigureAwait(false);
        }

        await seed.InsertAsync("PackageValidations", new()
        {
            ["SubmissionId"] = Guid.NewGuid().ToString("D"), ["PackageId"] = "Legacy.Package", ["PackageVersion"] = "1.0.0",
            ["Type"] = "Scan", ["State"] = 1, ["Created"] = created,
        }, token).ConfigureAwait(false);

        await seed.InsertAsync("AbuseReports", new()
        {
            ["PackageId"] = "Other.Package", ["PackageVersion"] = "1.0.0", ["Reason"] = "Other", ["Details"] = "seeded", ["Created"] = created,
        }, token).ConfigureAwait(false);
    }

    /// <summary>How the API key is stored (SHA-256, lowercase hex), duplicated here as the identity project is internal.</summary>
    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    internal sealed class Inserter
    {
        private readonly DbConnection _connection;
        private readonly string _engine;

        public Inserter(DbConnection connection, string engine)
        {
            _connection = connection;
            _engine = engine;
        }

        public string Quote(string name) => _engine switch
        {
            "SqlServer" => "[" + name + "]",
            "MySql" => "`" + name + "`",
            _ => "\"" + name + "\"",
        };

        public async Task InsertAsync(string table, Dictionary<string, object?> values, CancellationToken token)
        {
            using var command = _connection.CreateCommand();
            var names = values.Keys.ToList();
            command.CommandText =
                $"INSERT INTO {Quote(table)} ({string.Join(", ", names.Select(Quote))}) VALUES ({string.Join(", ", names.Select((_, i) => "@p" + i))})";
            for (var i = 0; i < names.Count; i++)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@p" + i;
                parameter.Value = values[names[i]] ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        public async Task<int> MaxKeyAsync(string table, CancellationToken token)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT MAX({Quote("Key")}) FROM {Quote(table)}";

            return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
        }

        public async Task<int> PackageKeyAsync(string id, string version, CancellationToken token)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT {Quote("Key")} FROM {Quote("Packages")} WHERE {Quote("Id")} = @id AND {Quote("Version")} = @v";
            foreach (var (name, value) in new[] { ("@id", id), ("@v", version) })
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
        }
    }
}
