using System;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Database.MySql;

/// <summary>
/// Opens a <see cref="MySqlContext"/> on any MySql connection string; see <see cref="IDatabaseContextFactory"/>.
/// </summary>
public class MySqlDatabaseContextFactory : IDatabaseContextFactory
{
    /// <inheritdoc/>
    public string DatabaseType => "MySql";

    /// <inheritdoc/>
    public IContext Create(
        string connectionString,
        DatabaseOptions? options = null,
        Action<DbContextOptionsBuilder>? configure = null)
    {
        var builder = new DbContextOptionsBuilder<MySqlContext>();

        Configure(builder, connectionString, options?.MySqlServerVersion);
        configure?.Invoke(builder);

        return new MySqlContext(builder.Options);
    }

    /// <summary>
    /// The one place the provider is configured: used by both this factory and the application's
    /// registration, so a second database is configured exactly like the main one.
    /// </summary>
    internal static void Configure(DbContextOptionsBuilder options, string? connectionString, string? mySqlServerVersion)
    {
        // Detecting the version connects to the server, and these options are also built whenever the
        // contexts are enumerated (including by the design-time tools). Prefer the configured version so
        // that building options stays offline; otherwise detect it against this connection string.
        var serverVersion = string.IsNullOrWhiteSpace(mySqlServerVersion)
            ? ServerVersion.AutoDetect(connectionString)
            : ServerVersion.Parse(mySqlServerVersion);

        options.UseMySql(connectionString, serverVersion);
    }
}
