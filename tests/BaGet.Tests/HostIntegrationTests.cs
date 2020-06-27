using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Database.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests
{
    public class HostIntegrationTests
    {
        private readonly string DatabaseTypeKey = "Database:Type";
        private readonly string ConnectionStringKey = "Database:ConnectionString";

        [Fact]
        public void ThrowsIfDatabaseTypeInvalid()
        {
            var provider = BuildServiceProvider(new Dictionary<string, string>
            {
                { DatabaseTypeKey, "InvalidType" }
            });

            Assert.Throws<InvalidOperationException>(
                () => provider.GetRequiredService<IContext>());
        }

        [Fact]
        public void ReturnsDatabaseContext()
        {
            var provider = BuildServiceProvider(new Dictionary<string, string>
            {
                { DatabaseTypeKey, "Sqlite" },
                { ConnectionStringKey, "..." }
            });

            Assert.NotNull(provider.GetRequiredService<IContext>());
        }

        [Fact]
        public void ReturnsSqliteContext()
        {
            var provider = BuildServiceProvider(new Dictionary<string, string>
            {
                { DatabaseTypeKey, "Sqlite" },
                { ConnectionStringKey, "..." }
            });

            Assert.NotNull(provider.GetRequiredService<SqliteContext>());
        }

        [Fact]
        public void DefaultsToSqlite()
        {
            var provider = BuildServiceProvider();

            var context = provider.GetRequiredService<IContext>();

            Assert.IsType<SqliteContext>(context);
        }

        private IServiceProvider BuildServiceProvider(Dictionary<string, string> configs = null)
        {
            var host = Program
                .CreateHostBuilder(new string[0])
                .ConfigureAppConfiguration((ctx, config) =>
                {
                    config.AddInMemoryCollection(configs ?? new Dictionary<string, string>());
                })
                .Build();

            return host.Services;
        }
    }
}
