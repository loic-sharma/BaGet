using System;
using BaGetter.Core;
using BaGetter.Database.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGetter
{
    public static class SqlServerApplicationExtensions
    {
        public static BaGetterApplication AddSqlServerDatabase(this BaGetterApplication app)
        {
            app.Services.AddBaGetDbContextProvider<SqlServerContext>("SqlServer", (provider, options) =>
            {
                var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

                options.UseSqlServer(databaseOptions.Value.ConnectionString);
            });

            return app;
        }

        public static BaGetterApplication AddSqlServerDatabase(
            this BaGetterApplication app,
            Action<DatabaseOptions> configure)
        {
            app.AddSqlServerDatabase();
            app.Services.Configure(configure);
            return app;
        }
    }
}
