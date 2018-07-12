using System;
using BaGet.Core.Configuration;
using BaGet.Core.Entities;
using BaGet.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Extensions
{
    // TODO: Considering moving this to BaGet.Core
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBaGetContext(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            services.AddScoped<IContext>(provider =>
            {
                var databaseOptions = provider.GetRequiredService<IOptions<BaGetOptions>>()
                    .Value
                    .Database;
                if (databaseOptions == null)
                {
                    throw new InvalidOperationException($"The '{nameof(BaGetOptions.Database)}' configuration is missing");
                }
                switch (databaseOptions.Type)
                {
                    case DatabaseType.Sqlite:
                        return provider.GetRequiredService<SqliteContext>();

                    case DatabaseType.SqlServer:
                        return provider.GetRequiredService<SqlServerContext>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported database provider: {databaseOptions.Type}");
                }
            });

            services.AddDbContext<SqliteContext>((provider, options) =>
            {
                var databaseOptions = provider.GetRequiredService<IOptions<BaGetOptions>>()
                    .Value
                    .Database;
                if (string.IsNullOrEmpty(databaseOptions.ConnectionString))
                {
                    throw new InvalidOperationException($"The '{nameof(databaseOptions.ConnectionString)}' configuration is missing");
                }
                options.UseSqlite(databaseOptions.ConnectionString);//required Syntax: "Data Source=Filename"!!!
            });

            services.AddDbContext<SqlServerContext>((provider, options) =>
            {
                var databaseOptions = provider.GetRequiredService<IOptions<BaGetOptions>>()
                    .Value
                    .Database;

                options.UseSqlServer(databaseOptions.ConnectionString);
            });

            return services;
        }
    }
}
