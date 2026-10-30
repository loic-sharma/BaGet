using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using BaGet.Core;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
#pragma warning disable ASPDEPR005

namespace BaGet;

/// <summary>
/// BaGet's options configuration, specific to the default BaGet application.
/// Don't use this if you are embedding BaGet into your own custom ASP.NET Core application.
/// </summary>
public class ConfigureBaGetOptions
    : IConfigureOptions<CorsOptions>
        , IConfigureOptions<FormOptions>
        , IConfigureOptions<ForwardedHeadersOptions>
        , IConfigureOptions<HostFilteringOptions>
        , IConfigureOptions<IISServerOptions>
        , IValidateOptions<BaGetOptions>
{
    public const string CorsPolicy = "AllowAll";

    /// <summary>The policy of <c>/api/ui</c>: the listed origins with credentials, or (no list) any origin without.</summary>
    public const string UiCorsPolicy = "UiApi";

    /// <summary>
    /// The policy of the endpoints that sign a visitor in or prepare for it (<c>/api/ui/session*</c>, <c>/antiforgery</c>, <c>/account/*</c>): the listed origins with credentials,
    /// or (no list) no cross-origin access at all, so that no page of another origin can script them from visitors' browsers.
    /// </summary>
    public const string UiSessionCorsPolicy = "UiApiSession";

    /// <summary>The origins of <c>Cors:AllowedOrigins</c>; an entry that is not an origin stops startup.</summary>
    public static IReadOnlyList<string> AllowedOrigins(IConfiguration configuration)
    {
        var origins = SessionReturnUrls.Origins(configuration.GetSection(nameof(BaGetOptions.Cors)).Get<CorsSettings>()?.AllowedOrigins);

        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || uri.AbsolutePath != "/")
            {
                throw new InvalidOperationException($"'Cors:AllowedOrigins' contains '{origin}', which is not an origin such as https://app.example.com.");
            }
        }

        return origins;
    }

    /// <summary>
    /// The <see cref="BaGetOptions.AllowedHosts"/> entry that disables host filtering.
    /// </summary>
    public const string AllowAnyHost = "*";

    /// <summary>The multipart body limit of an ordinary form (1 MiB); upload and push endpoints raise it.</summary>
    public const long DefaultFormLimit = 1024 * 1024;

    private readonly IConfiguration _configuration;

    public ConfigureBaGetOptions(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    // One source of truth: the lists the Settings page offers, declared by [AllowedSettingValues].
    private static readonly string[] ValidDatabaseTypes
        = AllowedSettingValuesAttribute.ValuesOf<DatabaseOptions>(nameof(DatabaseOptions.Type));

    private static readonly string[] ValidStorageTypes
        = AllowedSettingValuesAttribute.ValuesOf<StorageOptions>(nameof(StorageOptions.Type));

    private static readonly string[] ValidSearchTypes
        = AllowedSettingValuesAttribute.ValuesOf<SearchOptions>(nameof(SearchOptions.Type));

    private static bool IsValid(string[] allowed, string? value)
        => allowed.Contains(value, StringComparer.OrdinalIgnoreCase);

    public void Configure(CorsOptions options)
    {
        var origins = AllowedOrigins(_configuration);

        // Everything but /api/ui: any origin, never with credentials (the protocols authenticate with headers a page has to set itself).
        options.AddPolicy(
            CorsPolicy,
            builder => builder.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader()
                .WithExposedHeaders(SessionHeaders.All));

        if (origins.Count == 0)
        {
            options.AddPolicy(UiCorsPolicy, builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader().WithExposedHeaders(SessionHeaders.All));
            options.AddPolicy(UiSessionCorsPolicy, builder => builder.SetIsOriginAllowed(_ => false));

            return;
        }

        // Only the listed origins, and only they may send credentials (the cookie with its antiforgery header), and only on /api/ui.
        foreach (var name in new[] { UiCorsPolicy, UiSessionCorsPolicy })
        {
            options.AddPolicy(
                name,
                builder => builder.WithOrigins(origins.ToArray())
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders(SessionHeaders.All)
                    .AllowCredentials());
        }
    }

    public void Configure(FormOptions options)
    {
        // Antiforgery validation buffers the whole form of every POST, anonymous ones included: keep the default small.
        // The upload and push endpoints raise it themselves (see UploadSizeLimitAttribute).
        options.MultipartBodyLengthLimit = DefaultFormLimit;
    }

    public void Configure(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        var proxy = _configuration.GetSection(nameof(BaGetOptions.Proxy)).Get<ProxyOptions>() ?? new ProxyOptions();

        if (proxy.TrustAnyProxy)
        {
            // Explicit opt-in: believe every sender (only safe when clients cannot reach BaGet except through the proxy).
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            return;
        }

        // Default: the framework's loopback-only trust, plus what the operator listed.
        foreach (var address in SplitList(proxy.TrustedProxies))
        {
            if (!IPAddress.TryParse(address, out var ip))
            {
                throw new InvalidOperationException($"'Proxy:TrustedProxies' contains '{address}', which is not an IP address.");
            }

            options.KnownProxies.Add(ip);
        }

        foreach (var network in SplitList(proxy.TrustedNetworks))
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                throw new InvalidOperationException($"'Proxy:TrustedNetworks' contains '{network}', which is not a network in CIDR form (10.0.0.0/8).");
            }

            options.KnownIPNetworks.Add(parsed);
        }
    }

    private static IEnumerable<string> SplitList(string? value)
        => (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void Configure(HostFilteringOptions options)
    {
        var allowedHosts = ParseAllowedHosts(_configuration[nameof(BaGetOptions.AllowedHosts)]);

        options.AllowedHosts = allowedHosts;

        // An absent or empty "Host" header cannot be on an allow-list, and the URLs
        // BaGet advertises are built from that header. Only tolerate it while host
        // filtering is disabled, which is how the middleware behaves out of the box.
        options.AllowEmptyHosts = allowedHosts.Contains(AllowAnyHost);
    }

    /// <summary>
    /// Turn a configured <see cref="BaGetOptions.AllowedHosts"/> value into the list the
    /// host filtering middleware expects. An absent or blank value means "allow any
    /// host", so that a deployment which never configured this keeps working.
    /// </summary>
    internal static List<string> ParseAllowedHosts(string? value)
    {
        var hosts = (value ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            // ASP.NET Core's own parsing of this key does not trim, which turns the
            // usual "a.example.com; b.example.com" spacing into patterns that match
            // nothing and locks out hosts the operator meant to allow.
            .Select(host => host.Trim())
            .Where(host => host.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hosts.Count == 0)
        {
            return new List<string> { AllowAnyHost };
        }

        return hosts;
    }

    public void Configure(IISServerOptions options)
    {
        options.MaxRequestBodySize = 262144000;
    }

    public ValidateOptionsResult Validate(string? name, BaGetOptions options)
    {
        var failures = new List<string>();

        if (options.Database == null) failures.Add($"The '{nameof(BaGetOptions.Database)}' config is required");
        if (options.Mirror == null) failures.Add($"The '{nameof(BaGetOptions.Mirror)}' config is required");
        if (options.Search == null) failures.Add($"The '{nameof(BaGetOptions.Search)}' config is required");
        if (options.Storage == null) failures.Add($"The '{nameof(BaGetOptions.Storage)}' config is required");

        if (!IsValid(ValidDatabaseTypes, options.Database?.Type))
        {
            failures.Add(
                $"The '{nameof(BaGetOptions.Database)}:{nameof(DatabaseOptions.Type)}' config is invalid. " +
                $"Allowed values: {string.Join(", ", ValidDatabaseTypes)}");
        }

        if (!IsValid(ValidStorageTypes, options.Storage?.Type))
        {
            failures.Add(
                $"The '{nameof(BaGetOptions.Storage)}:{nameof(StorageOptions.Type)}' config is invalid. " +
                $"Allowed values: {string.Join(", ", ValidStorageTypes)}");
        }

        if (!IsValid(ValidSearchTypes, options.Search?.Type))
        {
            failures.Add(
                $"The '{nameof(BaGetOptions.Search)}:{nameof(SearchOptions.Type)}' config is invalid. " +
                $"Allowed values: {string.Join(", ", ValidSearchTypes)}");
        }

        if (options.Telemetry != null) failures.AddRange(options.Telemetry.Validate());
        if (options.Ha != null) failures.AddRange(options.Ha.Validate());

        if (failures.Any()) return ValidateOptionsResult.Fail(failures);

        return ValidateOptionsResult.Success;
    }
}
