using System.Linq;
using BaGet.Core;
using BaGet.Web.V3.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing.Constraints;
using Scalar.AspNetCore;
using BaGet.Web.V3.Controllers.Protocols;
using BaGet.Web.V3.Filters;
using BaGet.Web.V3.Middleware;
using BaGet.Web.V3.Oci;
using BaGet.Web.V3.Routing;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet;

/// <summary>
/// Maps every endpoint. Each protocol route (everything but the Razor Pages) is registered twice: at its plain
/// path, which serves the <c>default</c> repository, and under <c>r/{repo}/</c>, which serves the named one
/// (<see cref="Routes.RepositoryPathPrefix"/>). <see cref="RepositoryResolutionMiddleware"/> turns the route value
/// into <see cref="BaGet.Core.IRepositoryContext"/>, and <see cref="BaGetUrlGenerator"/> picks the matching variant.
/// </summary>
public class BaGetEndpointBuilder
{
    /// <summary>
    /// The one place that registers a protocol route, so no endpoint can exist for the default repository only.
    /// <paramref name="format"/> is the format whose protocol the route speaks (its <see cref="IFormatModule"/>;
    /// the pattern starts with the module's <see cref="IFormatModule.RoutePrefix"/> for a format other than NuGet): a request
    /// for it in a repository of another format is answered 404.
    /// </summary>
    public static void MapRoute(
        IEndpointRouteBuilder endpoints,
        string name,
        string pattern,
        object defaults,
        object? constraints = null,
        bool authorizesItself = false,
        RepositoryFormat format = RepositoryFormat.NuGet,
        bool writesContent = true,
        bool serverRoot = false)
    {
        // The marker is what RepositoryReadFilter looks for: every route registered here needs Read access, except a
        // route that is constrained to write methods only (and then only for those methods).
        var metadata = RepositoryRouteMetadata.For(WritesOnly(constraints), authorizesItself, format, writesContent, resolvesRepositoryItself: serverRoot);
        endpoints.MapControllerRoute(name, pattern, defaults, constraints).WithMetadata(metadata);

        // A server-root route (the OCI registry's /v2/) is registered once, at the root: the repository is part of its path
        // ({repo}), not of an r/{repo}/ prefix, and it finds and authorizes the repository itself.
        if (serverRoot) return;

        endpoints.MapControllerRoute(
            Routes.RepositoryRouteName(name),
            Routes.RepositoryPathPrefix + pattern,
            defaults,
            constraints).WithMetadata(metadata);
    }

    /// <summary>
    /// Whether the <c>httpMethod</c> constraint admits write methods (PUT, POST, PATCH, DELETE) only. An unconstrained
    /// route, or one that admits GET or HEAD, is a read route whatever method the caller sends.
    /// </summary>
    private static bool WritesOnly(object? constraints)
    {
        if (constraints == null) return false;

        var values = new RouteValueDictionary(constraints);
        if (!values.TryGetValue("httpMethod", out var constraint) || constraint is not HttpMethodRouteConstraint method) return false;

        return method.AllowedMethods.Count > 0
            && method.AllowedMethods.All(m => !HttpMethods.IsGet(m) && !HttpMethods.IsHead(m)
                && !HttpMethods.IsOptions(m) && !HttpMethods.IsTrace(m));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Razor Pages (a host's own pages) exist only when the host registered the screens (AddBaGetFrontEnd calls
        // AddRazorPages); an API-only host has none, and MapRazorPages would throw without their services.
        if (endpoints.ServiceProvider.GetService<PageLoader>() != null) endpoints.MapRazorPages();

        MapServiceIndexRoutes(endpoints);
        MapPackagePublishRoutes(endpoints);
        MapSymbolRoutes(endpoints);
        MapSearchRoutes(endpoints);
        MapPackageMetadataRoutes(endpoints);
        MapPackageContentRoutes(endpoints);
        MapV2FeedRoutes(endpoints);
        MapPackageMetadataAdminRoutes(endpoints);
        MapVulnerabilityRoutes(endpoints);
        MapCatalogRoutes(endpoints);
        MapValidationRoutes(endpoints);
        MapNpmRoutes(endpoints);
        MapMavenRoutes(endpoints);
        MapOciRoutes(endpoints);
        MapAdminApiRoutes(endpoints);
        MapApiDocumentation(endpoints);
        MapHealthRoutes(endpoints);
    }

    /// <summary>
    /// <c>/health/live</c> (the process answers: always 200) and <c>/health/ready</c> (<see cref="ReadinessService"/>: the database, the
    /// storage and, in a cluster, the shared store; 503 when one fails) for a load balancer. Anonymous and carrying no repository, so they
    /// are not repository routes; the body names each check and whether it passed, never a connection string or message.
    /// </summary>
    public void MapHealthRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("health/live", () => Results.Text("Healthy", "text/plain"));
        endpoints.MapGet("health/ready", async (ReadinessService readiness, HttpContext context) =>
        {
            var report = await readiness.CheckAsync(force: false, context.RequestAborted);
            var body = new
            {
                status = report.IsReady ? "Healthy" : "Unhealthy",
                checks = report.Probes.ToDictionary(p => p.Name, p => p.Healthy ? "Healthy" : "Unhealthy"),
            };

            return Results.Json(body, statusCode: report.IsReady ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });
    }

    /// <summary>
    /// The administration REST API (<c>/api/v1</c>, <see cref="AdminApiController"/>): attribute-routed controllers, so
    /// <c>MapControllers</c> maps them (the protocol controllers are mapped through <c>MapRoute</c> and carry no route attribute; the
    /// other attribute-routed controllers are the UI controllers (<c>BaGet.Identity.Controllers.Base.UiControllerBase</c>),
    /// which get their <c>r/{repo}/</c> twin from <see cref="RepositoryActionRouteConvention"/>). The API controllers are not repository
    /// routes: they carry no <see cref="RepositoryRouteMetadata"/>, no <c>r/{repo}/</c> twin, and authorize themselves with an
    /// administrator's API key.
    /// </summary>
    public void MapAdminApiRoutes(IEndpointRouteBuilder endpoints) => endpoints.MapControllers();

    /// <summary>
    /// The OpenAPI documents (<c>/openapi/ui.json</c>, <c>/openapi/v1.json</c>, <c>/openapi/protocols.json</c>), the alias
    /// <c>/api/v1/openapi.json</c> (the <c>v1</c> document) and the Scalar viewer at <c>/scalar</c>. Everything is mapped in one group
    /// whose filter answers 404 to anyone but an administrator (<see cref="ApiDocumentationAccess"/>). Not
    /// repository routes: no <see cref="RepositoryRouteMetadata"/>, no <c>r/{repo}/</c> twin.
    /// </summary>
    public void MapApiDocumentation(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(string.Empty);
        group.AddEndpointFilter(async (context, next) =>
            await BaGet.Web.V3.OpenApi.ApiDocumentationAccess.IsAllowedAsync(context.HttpContext, context.HttpContext.RequestAborted)
                ? await next(context)
                : Results.NotFound());

        group.MapOpenApi("/openapi/{documentName}.json");
        // No {documentName} in the pattern: the default document, v1 (the administration API).
        group.MapOpenApi(BaGet.Web.V3.Api.Catalog.ApiCatalog.Root + "/openapi.json");
        group.MapScalarApiReference(BaGet.Web.V3.OpenApi.ApiDocuments.ScalarPath, options =>
        {
            options.WithTitle("BaGet API");
            foreach (var name in BaGet.Web.V3.OpenApi.ApiDocuments.All) options.AddDocument(name, routePattern: BaGet.Web.V3.OpenApi.ApiDocuments.JsonPath(name));
        });
    }

    /// <summary>
    /// The OCI Distribution Spec (<see cref="RepositoryFormat.Docker"/>), which a client addresses at the server root: <c>/v2/</c>
    /// (version check), <c>/v2/token</c> (the <c>docker login</c> token service) and <c>/v2/{repo}/{image...}/blobs|manifests|tags|referrers/...</c>,
    /// where <c>repo</c> is the BaGet repository and the rest is the image name and what is asked of it (the name has any number
    /// of slashes, so <see cref="OciNames.Parse"/> splits it from the right). Registered once each (no <c>r/{repo}/</c> twin) and
    /// <see cref="RepositoryRouteMetadata.ResolvesRepositoryItself"/>: <see cref="OciAuthorizationFilter"/> picks the
    /// repository, authorizes it with a registry token or an API key, and answers in the registry's error format.
    /// </summary>
    public void MapOciRoutes(IEndpointRouteBuilder endpoints)
    {
        void Map(string name, string pattern, string action, params string[] methods)
            => MapRoute(
                endpoints,
                name,
                pattern,
                new { controller = "Oci", action },
                new { httpMethod = new HttpMethodRouteConstraint(methods) },
                format: RepositoryFormat.Docker,
                serverRoot: true);

        Map(Routes.OciBaseRouteName, "v2", "Base", "GET", "HEAD");
        Map(Routes.OciBaseSlashRouteName, "v2/", "Base", "GET", "HEAD");
        Map(Routes.OciTokenRouteName, "v2/token", "Token", "GET", "POST");

        var pattern = "v2/{" + Routes.RepositoryRouteKey + "}/{**path}";
        Map(Routes.OciGetRouteName, pattern, "Get", "GET", "HEAD");
        Map(Routes.OciPostRouteName, pattern, "Post", "POST");
        Map(Routes.OciPatchRouteName, pattern, "Patch", "PATCH");
        Map(Routes.OciPutRouteName, pattern, "Put", "PUT");
        Map(Routes.OciDeleteRouteName, pattern, "Delete", "DELETE");
    }

    /// <summary>
    /// The Maven 2 repository layout (<see cref="RepositoryFormat.Maven"/>): everything below <c>maven/</c> of a repository's base,
    /// so a repository <c>libs</c> is at <c>/r/libs/maven/</c>. One catch-all path per method: reads pass
    /// <see cref="RepositoryReadFilter"/>; <c>PUT</c> and <c>DELETE</c> authorize themselves (see <see cref="MavenController"/>).
    /// </summary>
    public void MapMavenRoutes(IEndpointRouteBuilder endpoints)
    {
        void Map(string name, string action, params string[] methods)
            => MapRoute(
                endpoints,
                name,
                "maven/{**path}",
                new { controller = "Maven", action },
                new { httpMethod = new HttpMethodRouteConstraint(methods) },
                format: RepositoryFormat.Maven);

        Map(Routes.MavenGetRouteName, "Get", "GET", "HEAD");
        Map(Routes.MavenPutRouteName, "Put", "PUT");
        Map(Routes.MavenDeleteRouteName, "Delete", "DELETE");
    }

    /// <summary>
    /// The npm registry (<see cref="RepositoryFormat.Npm"/>): everything below <c>npm/</c> of a repository's base, so a repository
    /// <c>web</c> is a registry at <c>/r/web/npm/</c>. A package name is one segment (<c>{name}</c>: <c>left-pad</c>, or the
    /// scoped <c>@scope%2fname</c> npm sends) or, for a client that writes the slash, the two segments <c>@{scope}/{pkg}</c>:
    /// every route that names a package is registered in both shapes. Routes that only accept writes are constrained to their
    /// methods (they authorize themselves, see <see cref="NpmController"/>); the others pass <see cref="RepositoryReadFilter"/>.
    /// </summary>
    public void MapNpmRoutes(IEndpointRouteBuilder endpoints)
    {
        void Map(string name, string pattern, string action, string? method, bool perPackage, bool writesContent = true)
        {
            object? constraints = method == null ? null : new { httpMethod = new HttpMethodRouteConstraint(method) };
            var defaults = new { controller = "Npm", action };

            MapRoute(endpoints, name, "npm/" + pattern.Replace("{package}", "{name}"), defaults, constraints, format: RepositoryFormat.Npm, writesContent: writesContent);
            if (perPackage)
            {
                MapRoute(endpoints, name + Routes.NpmScopedSuffix, "npm/" + pattern.Replace("{package}", "@{scope}/{pkg}"), defaults, constraints, format: RepositoryFormat.Npm);
            }
        }

        Map(Routes.NpmPingRouteName, "-/ping", "Ping", "GET", false);
        Map(Routes.NpmWhoamiRouteName, "-/whoami", "Whoami", "GET", false);
        Map(Routes.NpmSearchRouteName, "-/v1/search", "Search", "GET", false);
        Map(Routes.NpmWebLoginRouteName, "-/v1/login", "WebLogin", "POST", false);
        Map(Routes.NpmLoginRouteName, "-/user/{userKey}", "Login", "PUT", false, writesContent: false);
        Map(Routes.NpmLogoutRouteName, "-/user/token/{token}", "Logout", "DELETE", false, writesContent: false);

        // Not constrained: the audit calls are POSTs that only read, so they pass the read check like a GET.
        Map(Routes.NpmAuditBulkRouteName, "-/npm/v1/security/advisories/bulk", "AuditBulk", null, false);
        Map(Routes.NpmAuditQuickRouteName, "-/npm/v1/security/audits/quick", "AuditQuick", null, false);

        Map(Routes.NpmDistTagsRouteName, "-/package/{package}/dist-tags", "DistTags", "GET", true);
        Map(Routes.NpmSetDistTagRouteName, "-/package/{package}/dist-tags/{tag}", "SetDistTag", "PUT", true);
        Map(Routes.NpmRemoveDistTagRouteName, "-/package/{package}/dist-tags/{tag}", "RemoveDistTag", "DELETE", true);

        Map(Routes.NpmPackumentRouteName, "{package}", "Packument", "GET", true);
        Map(Routes.NpmPublishRouteName, "{package}", "Put", "PUT", true);
        Map(Routes.NpmPutRevisionRouteName, "{package}/-rev/{rev}", "PutRevision", "PUT", true);
        Map(Routes.NpmDeletePackageRouteName, "{package}/-rev/{rev}", "DeletePackage", "DELETE", true);
        Map(Routes.NpmTarballRouteName, "{package}/-/{file}", "Tarball", "GET", true);
        Map(Routes.NpmDeleteTarballRouteName, "{package}/-/{file}/-rev/{rev}", "DeleteTarball", "DELETE", true);
    }

    /// <summary>
    /// Deprecation and vulnerability metadata for a package version. Writes only, so they
    /// share the authorisation of the publish endpoints.
    /// </summary>
    public void MapPackageMetadataAdminRoutes(IEndpointRouteBuilder endpoints)
    {
        MapRoute(
            endpoints,
            name: Routes.SetDeprecationRouteName,
            pattern: "v3/package/{id}/{version}/deprecation",
            defaults: new { controller = "PackageMetadataAdmin", action = "SetDeprecation" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

        MapRoute(
            endpoints,
            name: Routes.RemoveDeprecationRouteName,
            pattern: "v3/package/{id}/{version}/deprecation",
            defaults: new { controller = "PackageMetadataAdmin", action = "RemoveDeprecation" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("DELETE") });

        MapRoute(
            endpoints,
            name: Routes.SetVulnerabilitiesRouteName,
            pattern: "v3/package/{id}/{version}/vulnerabilities",
            defaults: new { controller = "PackageMetadataAdmin", action = "SetVulnerabilities" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });
    }

    public void MapValidationRoutes(IEndpointRouteBuilder endpoints)
    {
        MapRoute(
            endpoints,
            name: Routes.RepositorySignaturesRouteName,
            pattern: "v3/repository-signatures/index.json",
            defaults: new { controller = "RepositorySignatures", action = "Index" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });

        MapRoute(
            endpoints,
            name: Routes.RepositorySigningCertificateRouteName,
            pattern: "v3/repository-signatures/{fingerprint}.crt",
            defaults: new { controller = "RepositorySignatures", action = "Certificate" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });

        MapRoute(
            endpoints,
            name: Routes.ValidationStatusRouteName,
            pattern: "v3/package/{id}/{version}/validation",
            defaults: new { controller = "ValidationStatus", action = "Get" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") },
            authorizesItself: true);
    }

    public void MapCatalogRoutes(IEndpointRouteBuilder endpoints)
    {
        MapRoute(
            endpoints,
            name: Routes.CatalogIndexRouteName,
            pattern: "v3/catalog/index.json",
            defaults: new { controller = "Catalog", action = "Index" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });

        MapRoute(
            endpoints,
            name: Routes.CatalogPageRouteName,
            pattern: "v3/catalog/page{pageIndex:int}.json",
            defaults: new { controller = "Catalog", action = "Page" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });

        MapRoute(
            endpoints,
            name: Routes.CatalogLeafRouteName,
            pattern: "v3/catalog/data/{commitId}.json",
            defaults: new { controller = "Catalog", action = "Leaf" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });
    }

    public void MapVulnerabilityRoutes(IEndpointRouteBuilder endpoints)
    {
        MapRoute(
            endpoints,
            name: Routes.VulnerabilityIndexRouteName,
            pattern: "v3/vulnerabilities/index.json",
            defaults: new { controller = "Vulnerability", action = "Index" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });

        MapRoute(
            endpoints,
            name: Routes.VulnerabilityPageRouteName,
            pattern: "v3/vulnerabilities/{pageName}.json",
            defaults: new { controller = "Vulnerability", action = "Page" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });
    }

    /// <summary>
    /// The V2 (OData) feed. The routes are always mapped; the controller itself
    /// answers 404 for every action when "Feeds:EnableV2" is off, so turning the feed
    /// off does not change which routes exist.
    /// </summary>
    public void MapV2FeedRoutes(IEndpointRouteBuilder endpoints)
    {
        MapRoute(
            endpoints,
            name: Routes.V2RootRouteName,
            pattern: "api/v2",
            defaults: new { controller = "V2Feed", action = "Root" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });

        MapRoute(
            endpoints,
            name: Routes.V2MetadataRouteName,
            pattern: "api/v2/$metadata",
            defaults: new { controller = "V2Feed", action = "Metadata" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });

        MapRoute(
            endpoints,
            name: Routes.V2FindPackagesByIdRouteName,
            pattern: "api/v2/FindPackagesById()",
            defaults: new { controller = "V2Feed", action = "FindPackagesById" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });

        MapRoute(
            endpoints,
            name: Routes.V2SearchRouteName,
            pattern: "api/v2/Search()",
            defaults: new { controller = "V2Feed", action = "Search" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });

        // The keyed form carries its values inside the segment, so it is matched by a
        // literal pattern per argument order rather than by route values.
        MapRoute(
            endpoints,
            name: Routes.V2PackagesByKeyRouteName,
            pattern: "api/v2/Packages(Id={id},Version={version})",
            defaults: new { controller = "V2Feed", action = "PackagesByKey" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });

        MapRoute(
            endpoints,
            name: Routes.V2PackagesRouteName,
            pattern: "api/v2/Packages()",
            defaults: new { controller = "V2Feed", action = "Packages" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("GET") });
    }

    public void MapServiceIndexRoutes(IEndpointRouteBuilder endpoints)
    {
        MapRoute(
            endpoints,
            name: Routes.IndexRouteName,
            pattern: "v3/index.json",
            defaults: new { controller = "ServiceIndex", action = "Get" });
    }

    public void MapPackagePublishRoutes(IEndpointRouteBuilder endpoints)
    {
        MapRoute(
            endpoints,
            name: Routes.UploadPackageRouteName,
            pattern: "api/v2/package",
            defaults: new { controller = "PackagePublish", action = "Upload" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

        MapRoute(
            endpoints,
            name: Routes.DeleteRouteName,
            pattern: "api/v2/package/{id}/{version}",
            defaults: new { controller = "PackagePublish", action = "Delete" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("DELETE") });

        MapRoute(
            endpoints,
            name: Routes.RelistRouteName,
            pattern: "api/v2/package/{id}/{version}",
            defaults: new { controller = "PackagePublish", action = "Relist" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("POST") });
    }

    public void MapSymbolRoutes(IEndpointRouteBuilder endpoints)
    {
        MapRoute(
            endpoints,
            name: Routes.UploadSymbolRouteName,
            pattern: "api/v2/symbol",
            defaults: new { controller = "Symbol", action = "Upload" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

        MapRoute(
            endpoints,
            name: Routes.SymbolDownloadRouteName,
            pattern: "api/download/symbols/{file}/{key}/{file2}",
            defaults: new { controller = "Symbol", action = "Get" });

        MapRoute(
            endpoints,
            name: Routes.PrefixedSymbolDownloadRouteName,
            pattern: "api/download/symbols/{prefix}/{file}/{key}/{file2}",
            defaults: new { controller = "Symbol", action = "Get" });
    }

    public void MapSearchRoutes(IEndpointRouteBuilder endpoints)
    {
        MapRoute(
            endpoints,
            name: Routes.SearchRouteName,
            pattern: "v3/search",
            defaults: new { controller = "Search", action = "Search" });

        MapRoute(
            endpoints,
            name: Routes.AutocompleteRouteName,
            pattern: "v3/autocomplete",
            defaults: new { controller = "Search", action = "Autocomplete" });

        // This is an unofficial API to find packages that depend on a given package.
        MapRoute(
            endpoints,
            name: Routes.DependentsRouteName,
            pattern: "v3/dependents",
            defaults: new { controller = "Search", action = "Dependents" });
    }

    public void MapPackageMetadataRoutes(IEndpointRouteBuilder endpoints)
    {
        MapRoute(
            endpoints,
            name: Routes.RegistrationIndexRouteName,
            pattern: "v3/registration/{id}/index.json",
            defaults: new { controller = "PackageMetadata", action = "RegistrationIndex" });

        MapRoute(
            endpoints,
            name: Routes.RegistrationLeafRouteName,
            pattern: "v3/registration/{id}/{version}.json",
            defaults: new { controller = "PackageMetadata", action = "RegistrationLeaf" });
    }

    public void MapPackageContentRoutes(IEndpointRouteBuilder endpoints)
    {
        MapRoute(
            endpoints,
            name: Routes.PackageVersionsRouteName,
            pattern: "v3/package/{id}/index.json",
            defaults: new { controller = "PackageContent", action = "GetPackageVersions" });

        MapRoute(
            endpoints,
            name: Routes.PackageDownloadRouteName,
            pattern: "v3/package/{id}/{version}/{idVersion}.nupkg",
            defaults: new { controller = "PackageContent", action = "DownloadPackage" });

        MapRoute(
            endpoints,
            name: Routes.PackageDownloadManifestRouteName,
            pattern: "v3/package/{id}/{version}/{id2}.nuspec",
            defaults: new { controller = "PackageContent", action = "DownloadNuspec" });

        MapRoute(
            endpoints,
            name: Routes.PackageDownloadReadmeRouteName,
            pattern: "v3/package/{id}/{version}/readme",
            defaults: new { controller = "PackageContent", action = "DownloadReadme" });

        MapRoute(
            endpoints,
            name: Routes.PackageDownloadIconRouteName,
            pattern: "v3/package/{id}/{version}/icon",
            defaults: new { controller = "PackageContent", action = "DownloadIcon" });
    }
}
