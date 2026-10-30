namespace BaGet.Core;

public class BaGetOptions
{
    /// <summary>
    /// The API Key required to authenticate package
    /// operations. If empty, package operations do not require authentication.
    /// </summary>
    [SensitiveSetting]
    public string? ApiKey { get; set; }

    /// <summary>
    /// The application root URL for usage in reverse proxy scenarios.
    /// </summary>
    [RestartRequired]
    public string? PathBase { get; set; }

    /// <summary>
    /// The host names BaGet will serve, separated by semicolons. A request whose
    /// "Host" header matches none of them is rejected with a 400 response, before it
    /// reaches a handler. An entry may be a wildcard subdomain ("*.example.com") and
    /// may carry a port, in which case the port must match too; an entry without a
    /// port matches any port.
    ///
    /// If empty, or if any entry is "*", every host is allowed. That is the default:
    /// BaGet is commonly run behind a reverse proxy on a host name it cannot know.
    ///
    /// This is ASP.NET Core's own "AllowedHosts" configuration key, so an existing
    /// value keeps working unchanged.
    /// See: https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/host-filtering
    /// </summary>
    [RestartRequired]
    public string? AllowedHosts { get; set; }

    /// <summary>
    /// If enabled, the database will be updated at app startup by running
    /// Entity Framework migrations. This is not recommended in production.
    /// </summary>
    [RestartRequired]
    public bool RunMigrationsAtStartup { get; set; } = true;

    /// <summary>
    /// How BaGet should interpret package deletion requests.
    /// </summary>
    public PackageDeletionBehavior PackageDeletionBehavior { get; set; } = PackageDeletionBehavior.Unlist;

    /// <summary>
    /// If enabled, pushing a package that already exists will replace the
    /// existing package.
    /// </summary>
    public bool AllowPackageOverwrites { get; set; } = false;

    /// <summary>
    /// If true, disables package pushing, deleting, and re-listing.
    /// </summary>
    public bool IsReadOnlyMode { get; set; } = false;

    /// <summary>
    /// The URLs the BaGet server will use.
    /// As per documentation <a href="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host?view=aspnetcore-3.1#server-urls">here (Server URLs)</a>.
    /// </summary>
    [RestartRequired]
    public string? Urls { get; set; }

    public DatabaseOptions? Database { get; set; }

    public StorageOptions? Storage { get; set; }

    public SearchOptions? Search { get; set; }

    public MirrorOptions? Mirror { get; set; }

    public RemoteOptions? Remote { get; set; }

    public NpmOptions? Npm { get; set; }

    public MavenOptions? Maven { get; set; }

    public OciOptions? Oci { get; set; }

    public ScanningOptions? Scanning { get; set; }

    public FeedsOptions? Feeds { get; set; }

    public AccountsOptions? Accounts { get; set; }

    public EmailOptions? Email { get; set; }

    public SiteOptions? Site { get; set; }

    public ValidationOptions? Validation { get; set; }

    public TelemetryOptions? Telemetry { get; set; }

    public HaOptions? Ha { get; set; }

    public ProxyOptions? Proxy { get; set; }

    public CorsSettings? Cors { get; set; }

    public FrontendOptions? Frontend { get; set; }

    public DataProtectionKeyOptions? DataProtection { get; set; }
}