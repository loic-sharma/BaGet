using BaGet.Core;
using BaGet.Ha;
using BaGet.Identity.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
#pragma warning disable ASPDEPR003

namespace BaGet;

/// <summary>The host's web server limits and service registrations.</summary>
internal static class HostServiceCollectionExtensions
{
    /// <summary>Remove Kestrel's upload limit (a reverse proxy such as IIS can enforce one).</summary>
    public static IWebHostBuilder ConfigureBaGetWebHost(this IWebHostBuilder web)
    {
        web.ConfigureKestrel(options =>
        {
            // Remove the upload limit from Kestrel. If needed, an upload limit can
            // be enforced by a reverse proxy server, like IIS.
            options.Limits.MaxRequestBodySize = null;
        });

        return web;
    }

    /// <summary>
    /// Register BaGet with every subsystem implementation the host ships (databases, storage, search), accounts,
    /// cluster mode and the options validation.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration, read at registration (accounts, cluster mode).</param>
    /// <param name="overridesPath">The Settings page's overrides file (see <see cref="HostConfigurationExtensions.AddBaGetSettingsOverrides"/>).</param>
    public static IServiceCollection AddBaGetHost(this IServiceCollection services, IConfiguration configuration, string overridesPath)
    {
        services.AddSingleton(new SettingsOverridesStore(overridesPath));

        // Ideally we'd use:
        //
        //       services.ConfigureOptions<ConfigureBaGetOptions>();
        //
        //       However, "ConfigureOptions" doesn't register validations as expected.
        //       We'll instead register all these configurations manually.
        // See: https://github.com/dotnet/runtime/issues/38491
        services.AddTransient<IConfigureOptions<CorsOptions>, ConfigureBaGetOptions>();
        services.AddTransient<IConfigureOptions<FormOptions>, ConfigureBaGetOptions>();
        services.AddTransient<IConfigureOptions<ForwardedHeadersOptions>, ConfigureBaGetOptions>();
        services.AddTransient<IConfigureOptions<HostFilteringOptions>, ConfigureBaGetOptions>();
        services.AddTransient<IConfigureOptions<IISServerOptions>, ConfigureBaGetOptions>();
        services.AddTransient<IValidateOptions<BaGetOptions>, ConfigureBaGetOptions>();

        services.AddBaGetOptions<IISServerOptions>(nameof(IISServerOptions));
        services.AddBaGetWebApplication(app => app.AddBaGetProviders());

        // The server-rendered screens are optional (Frontend:Mvc, on by default): without them the host serves the APIs, and the application in Frontend:SpaPath.
        if (configuration.ReadFrontendOptions().Mvc) services.AddBaGetFrontEnd();
        else services.RemoveBaGetFrontEnd();

        // A wrong origin stops the start here, not at the first request.
        ConfigureBaGetOptions.AllowedOrigins(configuration);
        services.Configure<CorsSettings>(configuration.GetSection(nameof(BaGetOptions.Cors)));
        services.Configure<FrontendOptions>(configuration.GetSection(nameof(BaGetOptions.Frontend)));
        services.AddBaGetAccounts(configuration);

        // Cluster mode (Ha:Enabled): Redis, cluster-wide leases and the data protection keys in the database. After the BaGet
        // services, so the shared lease replaces the in-process one.
        services.AddBaGetHa(configuration);

        // A single node keeps its data protection key ring in a folder (cluster mode keeps it in the database, above).
        services.AddBaGetDataProtection(configuration, overridesPath);

        // You can swap between implementations of subsystems like storage and search using BaGet's configuration.
        // Each subsystem's implementation has a provider that reads the configuration to determine if it should be
        // activated. BaGet will run through all its providers until it finds one that is active.
        #pragma warning disable CS8634
        services.AddScoped(DependencyInjectionExtensions.GetServiceFromProviders<IContext>);
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IStorageService>);
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IPackageDatabase>);
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchService>);
        services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchIndexer>);
        #pragma warning restore CS8634

        // The search service registered just above is the one a virtual repository merges its members' searches through.
        services.AddRepositoryAwareSearch();

        services.AddSingleton<IConfigureOptions<MvcRazorRuntimeCompilationOptions>, ConfigureRazorRuntimeCompilation>();

        services.AddCors();

        return services;
    }

    /// <summary>The database, storage and search providers the host ships; configuration picks one of each at runtime.</summary>
    private static void AddBaGetProviders(this BaGetApplication app)
    {
        // Add database providers.
        app.AddAzureTableDatabase();
        app.AddMySqlDatabase();
        app.AddPostgreSqlDatabase();
        app.AddSqliteDatabase();
        app.AddSqlServerDatabase();

        // Add storage providers.
        app.AddFileStorage();
        app.AddAliyunOssStorage();
        app.AddAwsS3Storage();
        app.AddAzureBlobStorage();
        app.AddGoogleCloudStorage();

        // Add search providers.
        app.AddAzureSearch();
    }
}
