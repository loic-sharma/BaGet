namespace BaGet;

public static class MySqlApplicationExtensions
{
    public static BaGetApplication AddMySqlDatabase(this BaGetApplication app)
    {
        app.Services.AddBaGetDbContextProvider<MySqlContext>("MySql", (provider, options) =>
        {
            var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

            var serverVersion = new MySqlServerVersion(new Version(8, 0, 29));

            options.UseMySql(databaseOptions.Value.ConnectionString, serverVersion);
        });

        return app;
    }

    public static BaGetApplication AddMySqlDatabase(this BaGetApplication app, Action<DatabaseOptions> configure)
    {
        app.AddMySqlDatabase();
        app.Services.Configure(configure);
        return app;
    }
}
