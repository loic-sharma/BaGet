using System;
using BaGet.Core;
using BaGet.Database.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BaGet;

public static class SqliteApplicationExtensions
{
    public static BaGetApplication AddSqliteDatabase(this BaGetApplication app)
    {
        app.Services.AddBaGetDbContextProvider<SqliteContext>("Sqlite", (provider, options) =>
        {
            var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

            SqliteDatabaseContextFactory.Configure(
                options,
                databaseOptions.Value.ConnectionString,
                databaseOptions.Value.MySqlServerVersion);
        });

        app.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDatabaseContextFactory, SqliteDatabaseContextFactory>());

        return app;
    }

    public static BaGetApplication AddSqliteDatabase(
        this BaGetApplication app,
        Action<DatabaseOptions> configure)
    {
        app.AddSqliteDatabase();
        app.Services.Configure(configure);
        return app;
    }
}