using System;
using BaGet.Core;
using BaGet.Database.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet
{
    public static class SqliteApplicationExtensions
    {
        public static BaGetApplication AddSqliteDatabase(this BaGetApplication app)
        {
            app.Services.AddBaGetDbContextProvider<SqliteContext>("Sqlite", (provider, options) =>
            {
                var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

                options.UseSqlite(databaseOptions.Value.ConnectionString);
            });

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
}
