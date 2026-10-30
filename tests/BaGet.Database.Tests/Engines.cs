using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Database.MySql;
using BaGet.Database.PostgreSql;
using BaGet.Database.Sqlite;
using BaGet.Database.SqlServer;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace BaGet.Database.Tests;

/// <summary>
/// One database engine a copy can read from or write to: how to reach a brand-new, empty database of it.
/// </summary>
public sealed class Engine
{
    private readonly Func<string> _newDatabase;

    internal Engine(string type, IDatabaseContextFactory factory, string? mySqlServerVersion, Func<string> newDatabase)
    {
        Type = type;
        Factory = factory;
        MySqlServerVersion = mySqlServerVersion;
        _newDatabase = newDatabase;
    }

    /// <summary>The <c>Database:Type</c> value.</summary>
    public string Type { get; }

    public IDatabaseContextFactory Factory { get; }

    /// <summary>The <c>Database:MySqlServerVersion</c> to pass along, or null.</summary>
    public string? MySqlServerVersion { get; }

    /// <summary>A connection string to a database that does not exist yet (migrations create it).</summary>
    public string NewConnectionString() => _newDatabase();

    public DatabaseOptions Options => new() { Type = Type, MySqlServerVersion = MySqlServerVersion };
}

/// <summary>
/// Starts the SQL Server, PostgreSQL and MySQL test containers on first use and shares them for the whole run.
/// When Docker is not available the tests skip instead of failing, so the normal suite stays green without it.
/// </summary>
public static class Engines
{
    public static readonly string[] Types = { "Sqlite", "SqlServer", "PostgreSql", "MySql" };

    private static readonly ConcurrentDictionary<string, Lazy<Task<Engine>>> Started = new();
    private static readonly string SqliteDirectory = Path.Combine(Path.GetTempPath(), "baget-engines-" + Guid.NewGuid().ToString("N"));

    /// <summary>Every ordered pair of different engines, as xUnit theory data.</summary>
    public static IEnumerable<object[]> Pairs()
    {
        foreach (var from in Types)
        {
            foreach (var to in Types)
            {
                if (from != to) yield return new object[] { from, to };
            }
        }
    }

    /// <summary>The engine of <paramref name="type"/>, or skips the running test when it cannot be started (no Docker).</summary>
    public static async Task<Engine> GetOrSkipAsync(string type)
    {
        try
        {
            return await Started.GetOrAdd(type, t => new Lazy<Task<Engine>>(() => StartAsync(t))).Value.ConfigureAwait(false);
        }
        catch (Exception e) when (IsDockerUnavailable(e))
        {
            Assert.Skip($"{type} needs Docker, which is not available: {e.GetBaseException().Message}");
            throw;
        }
    }

    private static bool IsDockerUnavailable(Exception e)
    {
        for (var inner = e; inner != null; inner = inner.InnerException)
        {
            var name = inner.GetType().FullName ?? string.Empty;
            if (name.Contains("Docker", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static async Task<Engine> StartAsync(string type)
    {
        switch (type)
        {
            case "Sqlite":
                Directory.CreateDirectory(SqliteDirectory);
                return new Engine(type, new SqliteDatabaseContextFactory(), null,
                    () => "Data Source=" + Path.Combine(SqliteDirectory, Guid.NewGuid().ToString("N") + ".db"));

            case "SqlServer":
            {
                var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
                await container.StartAsync().ConfigureAwait(false);
                var server = container.GetConnectionString();
                return new Engine(type, new SqlServerDatabaseContextFactory(), null, () => WithDatabase(server, "Database"));
            }

            case "PostgreSql":
            {
                var container = new PostgreSqlBuilder("postgres:17").Build();
                await container.StartAsync().ConfigureAwait(false);
                var server = container.GetConnectionString();
                return new Engine(type, new PostgreSqlDatabaseContextFactory(), null, () => WithDatabase(server, "Database"));
            }

            case "MySql":
            {
                var container = new MySqlBuilder("mysql:8.4").WithUsername("root").WithPassword("rootpw").Build();
                await container.StartAsync().ConfigureAwait(false);
                var server = container.GetConnectionString();
                return new Engine(type, new MySqlDatabaseContextFactory(), "8.4.0", () => WithDatabase(server, "Database"));
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    /// <summary>The connection string with its database replaced by a fresh, uniquely named one.</summary>
    private static string WithDatabase(string connectionString, string key)
    {
        var name = "baget_" + Guid.NewGuid().ToString("N");
        var parts = new List<string>();
        var replaced = false;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            if (equals > 0 && part.Substring(0, equals).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(key + "=" + name);
                replaced = true;
            }
            else
            {
                parts.Add(part);
            }
        }

        if (!replaced) parts.Add(key + "=" + name);
        return string.Join(';', parts);
    }
}
