using BaGet.Database.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Core
{
    public static class PostgreSqlApplicationExtensions
    {
        public static void AddPostgreSql(this BaGetApplication app)
        {
            app.Services.AddBaGetDbContextProvider<PostgreSqlContext>("PostgreSql", (provider, options) =>
            {
                var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

                options.UseNpgsql(databaseOptions.Value.ConnectionString);
            });
        }
    }
}
