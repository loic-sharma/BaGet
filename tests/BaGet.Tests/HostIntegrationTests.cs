using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;
using BaGet.Database.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BaGet.Tests;

public class HostIntegrationTests
{
    private readonly string DatabaseTypeKey = "Database:Type";
    private readonly string ConnectionStringKey = "Database:ConnectionString";

    [Fact]
    public void ThrowsIfDatabaseTypeInvalid()
    {
        using var host = BuildHost(new Dictionary<string, string>
        {
            { DatabaseTypeKey, "InvalidType" }
        });
        using var scope = host.Services.CreateScope();

        Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IContext>());
    }

    [Fact]
    public void ReturnsDatabaseContext()
    {
        using var host = BuildHost(new Dictionary<string, string>
        {
            { DatabaseTypeKey, "Sqlite" },
            { ConnectionStringKey, "..." }
        });
        using var scope = host.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IContext>());
    }

    [Fact]
    public void ReturnsSqliteContext()
    {
        using var host = BuildHost(new Dictionary<string, string>
        {
            { DatabaseTypeKey, "Sqlite" },
            { ConnectionStringKey, "..." }
        });
        using var scope = host.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SqliteContext>());
    }

    [Fact]
    public void DefaultsToSqlite()
    {
        using var host = BuildHost();
        using var scope = host.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        Assert.IsType<SqliteContext>(context);
    }

    private IHost BuildHost(Dictionary<string, string>? configs = null)
    {
        // WebApplication.CreateBuilder reads the command line as a configuration source,
        // which is how these settings reach the host now that there is no host builder to
        // append a provider to.
        var args = (configs ?? new Dictionary<string, string>())
            .Select(setting => $"--{setting.Key}={setting.Value}")
            .ToArray();

        return Program.BuildApplication(args);
    }
}