using System;
using BaGet.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using BaGet.Identity.Application.Email;
using BaGet.Web.V3.Extensions;
using BaGet.Web.V3.Logging;

namespace BaGet;

/// <summary>The host's request pipeline and its startup warnings.</summary>
internal static class HostPipelineExtensions
{
    /// <summary>Configure the request pipeline, from host filtering to the endpoints.</summary>
    public static WebApplication UseBaGetPipeline(this WebApplication app)
    {
        var options = app.Configuration.Get<BaGetOptions>();

        // Take the Settings page's startup snapshot now, so that "pending restart" is judged
        // against what this process started with.
        app.Services.GetRequiredService<SettingsService>();

        // Reject a request whose "Host" header is not one BaGet serves, before anything
        // reads it. Every absolute URL BaGet advertises is built from that header, so a
        // forged host would otherwise decide where clients are sent.
        //
        // The default builder happens to add this middleware as well, but BaGet cannot
        // rely on that: a host built with a slim or empty builder does not, and the check
        // is BaGet's to own. Running it twice is a repeated string comparison, which is
        // cheaper than depending on the builder's choices.
        app.UseHostFiltering();

        app.WarnIfNothingAuthenticates(options);
        app.WarnIfEmailLinksCannotBeTrusted();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseStatusCodePages();
        }

        // The configured path base may be absent, "." meaning the root, or a path that
        // is missing its leading slash. Normalise it once so that startup cannot fail on
        // an unusable value and so both uses below agree on it.
        #pragma warning disable CS8602
        var pathBase = options.PathBase.NormalizePathBase();
        #pragma warning restore CS8602

        app.UseForwardedHeaders();
        app.UsePathBase(pathBase);

        // UsePathBase only sets the request's path base if the incoming path starts
        // with it. A reverse proxy may instead strip the path base before forwarding,
        // leaving nothing for UsePathBase to match, and BaGet would then advertise
        // URLs that bypass the proxy. Apply the configured path base in that case.
        if (pathBase.HasValue)
        {
            app.Use((context, next) =>
            {
                if (!context.Request.PathBase.HasValue)
                {
                    context.Request.PathBase = pathBase;
                }

                return next();
            });
        }

        // After forwarded headers (so the client address is the real one) and the path base, before
        // anything that answers: it logs the final status of every request, one line each. The default
        // message template carries the method, path (credentials in it masked) and status, never the query string or headers, so
        // "?apikey=" values and X-NuGet-ApiKey cannot reach the log; keep it that way.
        app.UseSerilogRequestLogging(requestLogging =>
        {
            requestLogging.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} from {ClientIp} responded {StatusCode} in {Elapsed:0.0} ms";
            // Without this the middleware writes to the static Log.Logger, which several hosts in one
            // process (the integration tests) would share and cross-feed.
            requestLogging.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
            requestLogging.GetLevel = GetRequestLogLevel;
            // The path is logged, so a credential carried in it (npm logout's token) is masked first.
            requestLogging.GetMessageTemplateProperties = (context, path, elapsed, statusCode) => new[]
            {
                new Serilog.Events.LogEventProperty("RequestMethod", new Serilog.Events.ScalarValue(context.Request.Method)),
                new Serilog.Events.LogEventProperty("RequestPath", new Serilog.Events.ScalarValue(RequestPathRedaction.Redact(path))),
                new Serilog.Events.LogEventProperty("StatusCode", new Serilog.Events.ScalarValue(statusCode)),
                new Serilog.Events.LogEventProperty("Elapsed", new Serilog.Events.ScalarValue(elapsed)),
            };
            requestLogging.EnrichDiagnosticContext = (diagnostics, context) =>
                diagnostics.Set("ClientIp", context.Connection.RemoteIpAddress?.ToString());
        });

        app.UseStaticFiles();

        // The layout and the package docs link the UI's static files (this host's wwwroot, packed as the Site package) at
        // /_content/BaGet.Web/..., the URL they had as static web assets of BaGet.Web.V3.
        app.UseStaticFiles(new StaticFileOptions { FileProvider = app.Environment.WebRootFileProvider, RequestPath = "/_content/BaGet.Web" });

        // A built single-page application (Frontend:SpaPath), under its own base path.
        app.UseBaGetSpaFiles();
        app.UseRouting();

        // Before authentication: /r/{repo}/ selects the repository every later query is filtered by.
        app.UseRepositoryResolution();

        // /api/ui may be called with credentials from the listed origins only; everything else keeps the no-credentials policy.
        app.UseWhen(context => IsUiSession(context.Request.Path), branch => branch.UseCors(ConfigureBaGetOptions.UiSessionCorsPolicy));
        app.UseWhen(context => IsUi(context.Request.Path) && !IsUiSession(context.Request.Path), branch => branch.UseCors(ConfigureBaGetOptions.UiCorsPolicy));
        app.UseWhen(context => !IsUi(context.Request.Path), branch => branch.UseCors(ConfigureBaGetOptions.CorsPolicy));
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseOperationCancelledMiddleware();
        app.UseMaintenanceMiddleware();

        // WebApplication is itself an endpoint route builder, so the endpoints are mapped
        // onto it directly rather than inside a UseEndpoints callback.
        new BaGetEndpointBuilder().MapEndpoints(app);

        // Last: index.html for a GET below the application's base path that nothing else answered.
        app.MapBaGetSpaFallback();

        return app;
    }

    private static bool IsUi(PathString path) => path.StartsWithSegments("/api/ui");

    private static bool IsUiSession(PathString path)
        => path.StartsWithSegments("/api/ui/session") || path.StartsWithSegments("/api/ui/antiforgery") || path.StartsWithSegments("/api/ui/account");

    /// <summary>
    /// Warning for a server error or an unhandled exception, Debug for static files (no endpoint matched,
    /// not an error) so they stay out of the default log, Information for everything else.
    /// </summary>
    public static Serilog.Events.LogEventLevel GetRequestLogLevel(this HttpContext context, double elapsed, Exception? exception)
    {
        if (exception != null || context.Response.StatusCode >= 500)
        {
            return Serilog.Events.LogEventLevel.Warning;
        }

        return context.GetEndpoint() == null && context.Response.StatusCode < 400
            ? Serilog.Events.LogEventLevel.Debug
            : Serilog.Events.LogEventLevel.Information;
    }

    /// <summary>
    /// Say once, at startup, that email was switched off because links in it could not be
    /// trusted: see <see cref="EmailLinkPolicy"/>.
    /// </summary>
    private static void WarnIfEmailLinksCannotBeTrusted(this WebApplication app)
    {
        var accounts = app.Configuration.GetSection(nameof(BaGetOptions.Accounts)).Get<AccountsOptions>();
        var email = app.Configuration.GetSection(nameof(BaGetOptions.Email)).Get<EmailOptions>();
        if (accounts?.Enabled != true || email?.IsConfigured != true) return;

        var site = app.Configuration.GetSection(nameof(BaGetOptions.Site)).Get<SiteOptions>();
        if (!EmailLinkPolicy.CanBuildLinks(site?.PublicUrl))
        {
            app.Logger.LogWarning(EmailLinkPolicy.UnavailableWarning);
        }
    }

    /// <summary>
    /// Say once, at startup, that this server accepts packages from anyone. With no
    /// global API key and no accounts there is nothing to tell one caller from another,
    /// so every push, overwrite and delete is open to whoever can reach the server. That
    /// is a legitimate configuration on a private network and the shipped default, which
    /// is exactly why it must not be silent.
    /// </summary>
    private static void WarnIfNothingAuthenticates(this WebApplication app, BaGetOptions? options)
    {
        #pragma warning disable CS8602
        if (options.IsReadOnlyMode) return;
        #pragma warning restore CS8602
        if (!string.IsNullOrEmpty(options.ApiKey)) return;

        var accounts = app.Configuration.GetSection(nameof(BaGetOptions.Accounts)).Get<AccountsOptions>();
        if (accounts?.Enabled == true)
        {
            // Accounts on and no global key: nothing authenticates a caller who sends no
            // key, by design - otherwise ownership could be bypassed - so a keyless push
            // (for example from CI written before accounts were on) gets a 401.
            app.Logger.LogWarning(
                "Accounts are enabled and no global \"ApiKey\" is configured, so keyless pushes "
                + "are refused with 401. Create a user API key on the \"API keys\" page and push "
                + "with it, or set \"ApiKey\".");
            return;
        }

        app.Logger.LogWarning(
            "No API key is configured and accounts are disabled, so anyone who can reach this "
            + "server may publish packages, claim package IDs and delete versions. Set \"ApiKey\", "
            + "or turn on \"Accounts:Enabled\", to require authentication.");
    }

    /// <summary>
    /// Turn a configured path base into one the request pipeline can use. An absent
    /// value, "." and "/" all mean "no path base"; anything else is given the leading
    /// slash that <see cref="PathString"/> requires.
    /// </summary>
    public static PathString NormalizePathBase(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PathString.Empty;
        }

        var trimmed = value.Trim().TrimEnd('/');
        if (trimmed.Length == 0 || trimmed == ".")
        {
            return PathString.Empty;
        }

        return new PathString(trimmed.StartsWith("/") ? trimmed : "/" + trimmed);
    }
}
