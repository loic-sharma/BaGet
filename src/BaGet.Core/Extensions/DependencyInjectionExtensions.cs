using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using BaGet.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

public static partial class DependencyInjectionExtensions
{
    public static IServiceCollection AddBaGetApplication(
        this IServiceCollection services,
        Action<BaGetApplication> configureAction)
    {
        var app = new BaGetApplication(services);

        services.AddConfiguration();
        services.TryAddSingleton<SettingsCatalog>();
        services.TryAddSingleton<SettingsService>();
        services.AddBaGetServices();
        services.AddDefaultProviders();

        configureAction(app);

        services.AddFallbackServices();

        return services;
    }

    /// <summary>
    /// Configures and validates options.
    /// </summary>
    /// <typeparam name="TOptions">The options type that should be added.</typeparam>
    /// <param name="services">The dependency injection container to add options.</param>
    /// <param name="key">
    /// The configuration key that should be used when configuring the options.
    /// If null, the root configuration will be used to configure the options.
    /// </param>
    /// <returns>The dependency injection container.</returns>
    public static IServiceCollection AddBaGetOptions<TOptions>(
        this IServiceCollection services,
        string? key = null)
        where TOptions : class
    {
        services.AddSingleton<IValidateOptions<TOptions>>(new ValidateBaGetOptions<TOptions>(key));

        // The Settings page lists every options type, provider-specific ones included.
        services.AddSingleton(new SettingsSchemaRegistration(typeof(TOptions), key));
        services.AddSingleton<IConfigureOptions<TOptions>>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            if (key != null)
            {
                config = config.GetSection(key);
            }

            return new BindOptions<TOptions>(config);
        });

        return services;
    }

    private static void AddConfiguration(this IServiceCollection services)
    {
        services.AddBaGetOptions<BaGetOptions>();
        services.AddBaGetOptions<DatabaseOptions>(nameof(BaGetOptions.Database));
        services.AddBaGetOptions<FileSystemStorageOptions>(nameof(BaGetOptions.Storage));
        services.AddBaGetOptions<MirrorOptions>(nameof(BaGetOptions.Mirror));
        services.AddBaGetOptions<RemoteOptions>(nameof(BaGetOptions.Remote));
        services.AddBaGetOptions<NpmOptions>(nameof(BaGetOptions.Npm));
        services.AddBaGetOptions<MavenOptions>(nameof(BaGetOptions.Maven));
        services.AddBaGetOptions<OciOptions>(nameof(BaGetOptions.Oci));
        services.AddBaGetOptions<ScanningOptions>(nameof(BaGetOptions.Scanning));
        services.AddBaGetOptions<FeedsOptions>(nameof(BaGetOptions.Feeds));
        services.AddBaGetOptions<AccountsOptions>(nameof(BaGetOptions.Accounts));
        services.AddBaGetOptions<EmailOptions>(nameof(BaGetOptions.Email));
        services.AddBaGetOptions<SiteOptions>(nameof(BaGetOptions.Site));
        services.AddBaGetOptions<ValidationOptions>(nameof(BaGetOptions.Validation));
        services.AddBaGetOptions<TelemetryOptions>(nameof(BaGetOptions.Telemetry));
        services.AddBaGetOptions<HaOptions>(nameof(BaGetOptions.Ha));
        services.AddBaGetOptions<SerilogOptions>("Serilog");
        services.AddBaGetOptions<SearchOptions>(nameof(BaGetOptions.Search));
        services.AddBaGetOptions<StorageOptions>(nameof(BaGetOptions.Storage));
    }

    private static void AddBaGetServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IFrameworkCompatibilityService, FrameworkCompatibilityService>();
        services.TryAddSingleton<IPackageDownloadsSource, PackageDownloadsJsonSource>();

        services.TryAddSingleton<ISearchResponseBuilder, SearchResponseBuilder>();
        services.TryAddSingleton<NuGetClient>();
        // Cluster mode (Ha:Enabled): the startup check runs first, the rest only does something when a store is registered.
        services.TryAddSingleton<HaBackendRules>();
        services.AddHostedService<HaStartupGuard>();
        services.TryAddSingleton<ClusterRuntime>();
        services.TryAddSingleton<ReadinessService>();
        services.TryAddSingleton<IClusterStatusService, ClusterStatusService>();
        services.AddHostedService<ClusterSyncService>();
        services.TryAddSingleton<MaintenanceGate>();
        services.TryAddSingleton<DatabaseCopyService>();
        services.AddHostedService<DatabaseCopyWorker>();
        services.TryAddSingleton<NullSearchIndexer>();
        services.TryAddSingleton<NullSearchService>();
        services.TryAddSingleton<RegistrationBuilder>();
        services.TryAddSingleton<SystemTime>();
        services.TryAddSingleton<ValidateStartupOptions>();

        services.TryAddSingleton(HttpClientFactory);
        services.TryAddSingleton(NuGetClientFactoryFactory);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormatModule, NuGetFormatModule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormatModule, NpmFormatModule>());
        services.TryAddSingleton(provider => new FormatRegistry(provider.GetServices<IFormatModule>()));
        services.TryAddScoped<NpmRegistryService>();
        services.TryAddScoped<NpmUpstreamClient>();
        services.TryAddScoped<RemoteNpmRegistry>();
        services.TryAddScoped<VirtualNpmRegistry>();
        services.TryAddScoped<INpmRegistry, RepositoryNpmRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormatModule, MavenFormatModule>());
        services.TryAddScoped<MavenRepositoryService>();
        services.TryAddScoped<MavenUpstreamClient>();
        services.TryAddScoped<RemoteMavenRepository>();
        services.TryAddScoped<VirtualMavenRepository>();
        services.TryAddScoped<IMavenRepository, RepositoryMavenRepository>();
        services.TryAddScoped<IMavenRepositoryAdministration, MavenRepositoryAdministration>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatSearchProvider, MavenFormatSearchProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormatModule, DockerFormatModule>());
        services.TryAddScoped<OciRegistryService>();
        services.TryAddScoped<OciUpstreamClient>();
        services.TryAddScoped<RemoteOciRegistry>();
        services.TryAddScoped<VirtualOciRegistry>();
        services.TryAddScoped<IOciRegistry, RepositoryOciRegistry>();
        services.TryAddScoped<IOciRequestAccess, OciRequestAccess>();
        services.TryAddScoped<IOciGarbageCollector, OciGarbageCollector>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAdminOperation, OciGarbageCollectionDryRunOperation>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAdminOperation, OciGarbageCollectionRunOperation>());
        services.TryAddSingleton<OsvState>();
        services.TryAddSingleton<ScanQueue>();
        services.TryAddSingleton<ScanPolicyCache>();
        services.TryAddSingleton<ITrivyRunner, ProcessTrivyRunner>();
        services.TryAddScoped<IVulnerabilitySource, OsvVulnerabilitySource>();
        services.TryAddScoped<IScanService, ScanService>();
        services.TryAddScoped<IScanPolicyService, ScanPolicyService>();
        services.TryAddScoped<IScanGate, ScanGate>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatScanner, NuGetFormatScanner>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatScanner, NpmFormatScanner>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatScanner, MavenFormatScanner>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatScanner, DockerFormatScanner>());
        services.AddHostedService<ScanWorker>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAdminOperation, ScanRepositoryOperation>());
        services.TryAddScoped<IOciAccess, OciAccess>();
        services.TryAddScoped<IOciTokenService, OciTokenService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatSearchProvider, DockerFormatSearchProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatSearchProvider, NpmFormatSearchProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatIndexer, NuGetFormatIndexer>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatSearchProvider, NuGetFormatSearchProvider>());
        services.TryAddScoped<IRepositoryContext, RepositoryContext>();
        services.TryAddScoped<IRepositoryService, RepositoryService>();
        services.TryAddScoped<IRepositoryAdministration, RepositoryAdministration>();
        services.TryAddSingleton<RemoteRepositoryState>();
        services.TryAddSingleton<IRemoteHttpClientFactory, RemoteHttpClientFactory>();
        services.TryAddSingleton(provider =>
        {
            // An explicitly registered protector wins; else the host's data protection (ASP.NET Core registers it);
            // else none, and credentials are refused rather than stored in clear.
            var protector = provider.GetService<IRemoteCredentialProtector>();
            var dataProtection = provider.GetService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();

            return new RemoteSettingsCodec(protector ?? (dataProtection == null ? null : new DataProtectionCredentialProtector(dataProtection)));
        });
        services.TryAddScoped<IRemoteUpstreamSource, RemoteUpstreamSource>();
        services.TryAddScoped<IRemoteRepositoryAdministration, RemoteRepositoryAdministration>();
        services.TryAddScoped<IRepositoryAccess, AccessRepositoryAccess>();
        services.TryAddSingleton<AccessModelVersion>();
        services.TryAddSingleton<AccessModelCache>();
        services.TryAddScoped<IAccessEvaluator, AccessEvaluator>();
        services.TryAddScoped<IAccessAdministration, AccessAdministration>();

        services.TryAddScoped<DownloadsImporter>();
        services.TryAddSingleton<IAdminAudit, LoggingAdminAudit>();
        services.TryAddSingleton<AdminJobService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAdminOperation, DownloadsImportOperation>());
        services.TryAddScoped<IAbuseReportAdministration, AbuseReportAdministration>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatRetentionHooks, NuGetRetentionHooks>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatRetentionHooks, NpmRetentionHooks>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatRetentionHooks, MavenRetentionHooks>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatRetentionHooks, DockerRetentionHooks>());
        services.TryAddSingleton<ArtifactUsageRecorder>();
        services.TryAddScoped<RetentionEngine>();
        services.TryAddScoped<IRetentionAdministration, RetentionAdministration>();
        services.TryAddSingleton<RetentionJobs>();
        services.AddHostedService<RetentionWorker>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatReplicator, NuGetReplicator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatReplicator, NpmReplicator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatReplicator, MavenReplicator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFormatReplicator, DockerReplicator>());
        // The in-process lease of a single node; with Ha:Enabled the host replaces it with ClusterJobLease (a lease in the shared store).
        services.TryAddSingleton<IJobLease, InProcessJobLease>();
        services.TryAddSingleton<IReplicationPeerFactory, ReplicationPeerFactory>();
        services.TryAddScoped<ReplicationEngine>();
        services.TryAddScoped<IReplicationInventory, ReplicationInventoryService>();
        services.TryAddScoped<IReplicationAdministration, ReplicationAdministration>();
        services.TryAddSingleton<ReplicationTrigger>();
        services.TryAddSingleton<ReplicationJobs>();
        services.AddHostedService<ReplicationWorker>();
        services.TryAddScoped<StatisticsService>();

        services.TryAddTransient<IAuthenticationService, ApiKeyAuthenticationService>();
        services.TryAddTransient<IPackageContentService, VirtualPackageContentService>();
        services.TryAddTransient<IPackageDeletionService, PackageDeletionService>();
        services.TryAddTransient<IPackageIndexingService, PackageIndexingService>();
        services.TryAddTransient<IPackageSubmissionService, DirectPackageSubmissionService>();
        services.TryAddTransient<PackageChangeNotifier>();
        services.TryAddTransient<IPackageAuthorizationService, AllowAuthenticatedAuthorizationService>();
        services.TryAddTransient<IPackageMetadataService, DefaultPackageMetadataService>();
        services.TryAddTransient<IPackageService, VirtualPackageService>();
        services.TryAddTransient<IPackageStagingService, StoragePackageStagingService>();
        services.TryAddTransient<IPackageStorageService, PackageStorageService>();
        services.TryAddTransient<IServiceIndexService, BaGetServiceIndex>();
        services.TryAddTransient<ISymbolIndexingService, SymbolIndexingService>();
        services.TryAddTransient<ISymbolStorageService, VirtualSymbolStorageService>();
        services.TryAddTransient<ISymbolRegistry, SymbolRegistry>();

        services.TryAddTransient<DatabaseSearchService>();
        services.TryAddTransient<FileStorageService>();
        services.TryAddTransient<PackageService>();
        services.TryAddTransient<V2UpstreamClient>();
        services.TryAddTransient<V3UpstreamClient>();
        services.TryAddTransient<DisabledUpstreamClient>();
        services.TryAddSingleton<NullStorageService>();
        services.TryAddTransient<PackageDatabase>();

        services.TryAddTransient(UpstreamClientFactory);

        services.AddVirtualRepositoryServices();
    }

    private static void AddDefaultProviders(this IServiceCollection services)
    {
        #pragma warning disable CS8621, CS8634
        services.AddProvider((provider, configuration) =>
        #pragma warning restore CS8621, CS8634
        {
            if (!configuration.HasSearchType("null")) return null;

            return provider.GetRequiredService<NullSearchService>();
        });

        #pragma warning disable CS8621, CS8634
        services.AddProvider((provider, configuration) =>
        #pragma warning restore CS8621, CS8634
        {
            if (!configuration.HasSearchType("null")) return null;

            return provider.GetRequiredService<NullSearchIndexer>();
        });

        services.AddProvider<IStorageService>((provider, configuration) =>
        {
            if (configuration.HasStorageType("filesystem"))
            {
                return provider.GetRequiredService<FileStorageService>();
            }

            if (configuration.HasStorageType("null"))
            {
                return provider.GetRequiredService<NullStorageService>();
            }

            #pragma warning disable CS8603
            return null;
            #pragma warning restore CS8603
        });
    }

    private static void AddFallbackServices(this IServiceCollection services)
    {
        services.TryAddScoped<IContext, NullContext>();

        // BaGet's services have multiple implementations that live side-by-side.
        // The application will choose the implementation using one of two ways:
        //
        // 1. Using the first implementation that was registered in the dependency injection
        //    container. This is the strategy used by applications that embed BaGet.
        // 2. Using "providers". The providers will examine the application's configuration to
        //    determine whether its service implementation is active. Thsi is the strategy used
        //    by the default BaGet application.
        //
        // BaGet has database and search services, but the database services are special
        // in that they may also act as search services. If an application registers the
        // database service first and the search service second, the application should
        // use the search service even though it wasn't registered first. Furthermore,
        // if an application registers a database service without a search service, the
        // database service should be used for search. This effect is achieved by deferring
        // the database search service's registration until the very end.
        services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPackageChangeHandler, SearchIndexChangeHandler>());
        services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<DatabaseSearchService>());

        // A virtual repository is searched through its members; a host that registers its own search service after this
        // calls AddRepositoryAwareSearch again (Program does).
        services.AddRepositoryAwareSearch();
    }

    private static HttpClient HttpClientFactory(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<MirrorOptions>>().Value;

        var assembly = Assembly.GetEntryAssembly();
        var assemblyName = assembly?.GetName().Name;
        var assemblyVersion = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        });

        client.DefaultRequestHeaders.Add("User-Agent", $"{assemblyName}/{assemblyVersion}");
        client.Timeout = TimeSpan.FromSeconds(options.PackageDownloadTimeoutSeconds);

        return client;
    }

    private static NuGetClientFactory NuGetClientFactoryFactory(IServiceProvider provider)
    {
        var httpClient = provider.GetRequiredService<HttpClient>();
        var options = provider.GetRequiredService<IOptions<MirrorOptions>>();

        return new NuGetClientFactory(
            httpClient,
            options.Value.PackageSource?.ToString());
    }

    private static IUpstreamClient UpstreamClientFactory(IServiceProvider provider)
    {
        // Every repository has the client of its own upstream: a remote repository's, the default repository's mirror
        // (Mirror:* through the "mirror" remote repository), or none. See RemoteUpstreamClient.
        return new RemoteUpstreamClient(
            provider.GetService<IRepositoryContext>(),
            provider.GetRequiredService<IRemoteUpstreamSource>(),
            provider.GetRequiredService<RemoteRepositoryState>(),
            provider.GetRequiredService<IRemoteHttpClientFactory>(),
            provider.GetRequiredService<ILoggerFactory>());
    }
}
