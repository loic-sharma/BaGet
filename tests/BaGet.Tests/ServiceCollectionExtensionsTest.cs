using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Core.Configuration;
using BaGet.Core.Entities;
using BaGet.Entities;
using BaGet.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests
{
    public class ServiceCollectionExtensionsTest
    {
        private readonly string DatabaseTypeKey = $"{nameof(BaGetOptions.Database)}:{nameof(DatabaseOptions.Type)}";
        private readonly string ConnectionStringKey = $"{nameof(BaGetOptions.Database)}:{nameof(DatabaseOptions.ConnectionString)}";

        [Fact]
        public void AskServiceProviderForNotConfiguredDatabaseOptions()
        {
            var provider = new ServiceCollection()
                .ConfigureBaGet(new ConfigurationBuilder().Build()) // Method Under Test
                .BuildServiceProvider();

            var expected = Assert.Throws<InvalidOperationException>(
                () => provider.GetRequiredService<IContext>().Query<Package>());

            Assert.Contains(nameof(BaGetOptions.Database), expected.Message);
        }

        [Fact]
        public void AskServiceProviderForWellConfiguredContext()
        {
            // Create a IConfiguration with a minimal "Database" object.
            var initialData = new Dictionary<string, string>();
            initialData.Add(DatabaseTypeKey, DatabaseType.Sqlite.ToString());
            initialData.Add(ConnectionStringKey, "Data Source=baget.db");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(initialData)
                .Build();

            var provider = new ServiceCollection()
                .ConfigureBaGet(configuration) // Method Under Test
                .BuildServiceProvider();

            Assert.NotNull(provider.GetRequiredService<IContext>());
        }

        [Theory]
        [InlineData("<invalid>")]
        [InlineData("")]
        [InlineData(" ")]
        public void AskServiceProviderForInvalidDatabaseType(string databaseType)
        {
            // Create a IConfiguration with a minimal "Database" object.
            var initialData = new Dictionary<string, string>();
            initialData.Add(DatabaseTypeKey, databaseType);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(initialData)
                .Build();

            var provider = new ServiceCollection()
                .ConfigureBaGet(configuration) // Method Under Test
                .BuildServiceProvider();

            var expected = Assert.Throws<InvalidOperationException>(
                () => provider.GetRequiredService<IContext>().Query<Package>());
        }
    }
}
