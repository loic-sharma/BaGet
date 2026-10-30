using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BaGet.Core;
using BaGet.Frontend.Routing;
using BaGet.Identity.Controllers.FrontEnd;
using BaGet.Web.V3.Controllers.FrontEnd;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace BaGet;

/// <summary>
/// The web front ends the host serves (<c>Frontend:*</c>): whether the server-rendered MVC screens are on, and a built single-page application served by BaGet
/// under <see cref="FrontendOptions.SpaBasePath"/> (static files, plus <c>index.html</c> for a GET below it that matches no file, so the application's router works).
/// </summary>
internal static class HostFrontendExtensions
{
    // First path segments that belong to BaGet: a single-page application there would hide their 404s.
    // (The screens' own prefixes are added from the mapped routes when the fallback is mapped: see MapBaGetSpaFallback.)
    private static readonly string[] ReservedSegments =
    {
        "api", "v3", "v2", "r", "scalar", "openapi", "health", "_content", "admin", "account", "packages", "upload", "profiles", "privacy", "terms",
        "npm", "maven", "symbols", "statistics", "stats", "index", "docker", "scan-artifact", "validation",
    };

    /// <summary>
    /// Takes the screens out of the MVC application: the Razor SDK registers the front-end assemblies the host references as application parts on its
    /// own (the host references <c>BaGet.Web.FrontEnd</c>), so not calling <c>AddBaGetFrontEnd</c> is not enough to leave them out.
    /// </summary>
    public static IServiceCollection RemoveBaGetFrontEnd(this IServiceCollection services)
    {
        var screens = new HashSet<Assembly>
        {
            typeof(RepositoryUrlHelper).Assembly,
            typeof(AdminUsersController).Assembly,
            typeof(HomeController).Assembly,
        };

        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            foreach (var part in manager.ApplicationParts.Where(p => p is AssemblyPart assembly && screens.Contains(assembly.Assembly)
                                                                    || p is CompiledRazorAssemblyPart compiled && screens.Contains(compiled.Assembly)).ToList())
            {
                manager.ApplicationParts.Remove(part);
            }
        });

        return services;
    }

    /// <summary>The <c>Frontend</c> section, validated.</summary>
    public static FrontendOptions ReadFrontendOptions(this IConfiguration configuration)
    {
        var options = configuration.GetSection(nameof(BaGetOptions.Frontend)).Get<FrontendOptions>() ?? new FrontendOptions();
        if (!string.IsNullOrWhiteSpace(options.SpaUrl)
            && (!Uri.TryCreate(options.SpaUrl.Trim(), UriKind.Absolute, out var spaUrl) || (spaUrl.Scheme != Uri.UriSchemeHttp && spaUrl.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(spaUrl.Query)))
        {
            throw new InvalidOperationException($"'Frontend:SpaUrl' ('{options.SpaUrl}') is not an absolute http(s) address without a query, such as https://app.example.com.");
        }

        if (string.IsNullOrWhiteSpace(options.SpaPath)) return options;

        var basePath = options.NormalizedBasePath();
        var first = basePath.Trim('/').Split('/')[0];
        if (basePath == "/" || Array.Exists(ReservedSegments, segment => string.Equals(segment, first, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"'Frontend:SpaBasePath' ('{options.SpaBasePath}') must be a path of its own such as '/app': the root and the paths BaGet serves ({string.Join(", ", ReservedSegments)}) cannot hold the application.");
        }

        options.SpaBasePath = basePath;

        return options;
    }

    /// <summary>The folder of the application, or null when none is configured. Fails at startup when it has no <c>index.html</c>.</summary>
    private static string? SpaFolder(FrontendOptions options, IWebHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(options.SpaPath)) return null;

        var folder = Path.GetFullPath(options.SpaPath, environment.ContentRootPath);
        if (!File.Exists(Path.Combine(folder, "index.html")))
        {
            throw new InvalidOperationException($"'Frontend:SpaPath' ('{options.SpaPath}', {folder}) is not a folder that contains index.html.");
        }

        return folder;
    }

    /// <summary>Serves the application's files under its base path (before routing; a file never shadows an endpoint because only the base path is served).</summary>
    public static WebApplication UseBaGetSpaFiles(this WebApplication app)
    {
        var options = app.Configuration.ReadFrontendOptions();
        var folder = SpaFolder(options, app.Environment);
        if (folder == null) return app;

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(folder),
            RequestPath = options.SpaBasePath,
            OnPrepareResponse = NoCacheForIndex,
        });

        return app;
    }

    /// <summary>
    /// Answers <c>index.html</c> for a GET below the base path that no file and no endpoint matched. A fallback endpoint has the lowest priority, so it never
    /// shadows a route of BaGet; a path with a file extension that is missing stays a 404.
    /// </summary>
    public static WebApplication MapBaGetSpaFallback(this WebApplication app)
    {
        var options = app.Configuration.ReadFrontendOptions();
        var folder = SpaFolder(options, app.Environment);
        if (folder == null) return app;

        // The first segment of every route BaGet mapped (screens, protocols, APIs): the application cannot live where one of them answers, so the fallback can never
        // answer index.html for a path an endpoint owns.
        var owned = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.PathSegments.FirstOrDefault())
            .Where(segment => segment != null && segment.Parts.All(part => part is RoutePatternLiteralPart))
            .Select(segment => ((RoutePatternLiteralPart)segment!.Parts[0]).Content)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var first = options.SpaBasePath.Trim('/').Split('/')[0];
        if (owned.Contains(first))
        {
            throw new InvalidOperationException($"'Frontend:SpaBasePath' ('{options.SpaBasePath}') starts with '{first}', which BaGet already serves: choose a path no route of BaGet starts with.");
        }

        var files = new StaticFileOptions { FileProvider = new PhysicalFileProvider(folder), OnPrepareResponse = NoCacheForIndex };
        app.MapFallbackToFile(options.SpaBasePath, "index.html", files).Add(builder => builder.Metadata.Add(new HttpMethodMetadata(new[] { HttpMethods.Get, HttpMethods.Head })));
        app.MapFallbackToFile(options.SpaBasePath + "/{*path:nonfile}", "index.html", files).Add(builder => builder.Metadata.Add(new HttpMethodMetadata(new[] { HttpMethods.Get, HttpMethods.Head })));

        return app;
    }

    private static void NoCacheForIndex(StaticFileResponseContext context)
    {
        if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
        }
    }
}
