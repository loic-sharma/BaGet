using BaGet.Core;
using BaGet.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;

namespace BaGet
{
    public class BaGetApi
    {
        public void MapRoutes(IEndpointRouteBuilder endpoints)
        {
            MapServiceIndexRoutes(endpoints);
            MapPackagePublishRoutes(endpoints);
            MapSymbolRoutes(endpoints);
            MapSearchRoutes(endpoints);
            MapPackageMetadataRoutes(endpoints);
            MapPackageContentRoutes(endpoints);
        }

        public void MapServiceIndexRoutes(IEndpointRouteBuilder endpoints)
        {
            endpoints
                .MapGet("v3/index.json", async context =>
                {
                    var cancellationToken = context.RequestAborted;
                    var serviceIndex = context.RequestServices.GetRequiredService<IServiceIndexService>();

                    var response = await serviceIndex.GetAsync(cancellationToken);

                    await context.Response.WriteAsJsonAsync(response, cancellationToken);
                })
                .WithRouteName(Routes.IndexRouteName);
        }

        public void MapPackagePublishRoutes(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapControllerRoute(
                name: Routes.UploadPackageRouteName,
                pattern: "api/v2/package",
                defaults: new { controller = "PackagePublish", action = "Upload" },
                constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

            endpoints.MapControllerRoute(
                name: Routes.DeleteRouteName,
                pattern: "api/v2/package/{id}/{version}",
                defaults: new { controller = "PackagePublish", action = "Delete" },
                constraints: new { httpMethod = new HttpMethodRouteConstraint("DELETE") });

            endpoints.MapControllerRoute(
                name: Routes.RelistRouteName,
                pattern: "api/v2/package/{id}/{version}",
                defaults: new { controller = "PackagePublish", action = "Relist" },
                constraints: new { httpMethod = new HttpMethodRouteConstraint("POST") });
        }

        public void MapSymbolRoutes(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapControllerRoute(
                name: Routes.UploadSymbolRouteName,
                pattern: "api/v2/symbol",
                defaults: new { controller = "Symbol", action = "Upload" },
                constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

            endpoints.MapControllerRoute(
                name: Routes.SymbolDownloadRouteName,
                pattern: "api/download/symbols/{file}/{key}/{file2}",
                defaults: new { controller = "Symbol", action = "Get" });

            endpoints.MapControllerRoute(
                name: Routes.PrefixedSymbolDownloadRouteName,
                pattern: "api/download/symbols/{prefix}/{file}/{key}/{file2}",
                defaults: new { controller = "Symbol", action = "Get" });
        }

        public void MapSearchRoutes(IEndpointRouteBuilder endpoints)
        {
            endpoints
                .MapGet("v3/search", async context =>
                {
                    var query = context.Request.ReadFromQuery("q");
                    var skip = context.Request.ReadFromQuery("skip", defaultValue: 0);
                    var take = context.Request.ReadFromQuery("take", defaultValue: 20);
                    var prerelease = context.Request.ReadFromQuery("prerelease", defaultValue: false);
                    var semVerLevel = context.Request.ReadFromQuery("semVerLevel", defaultValue: null);
                    var packageType = context.Request.ReadFromQuery("packageType", defaultValue: null);
                    var framework = context.Request.ReadFromQuery("framework", defaultValue: null);
                    var cancellationToken = context.RequestAborted;

                    var searchService = context.RequestServices.GetRequiredService<ISearchService>();
                    var includeSemVer2 = semVerLevel == "2.0.0";

                    var response = await searchService.SearchAsync(
                        query ?? string.Empty,
                        skip,
                        take,
                        prerelease,
                        includeSemVer2,
                        packageType,
                        framework,
                        cancellationToken);

                    await context.Response.WriteAsJsonAsync(response, cancellationToken);
                })
                .WithRouteName(Routes.SearchRouteName);

            endpoints
                .MapGet("v3/autocomplete", async context =>
                {
                    // TODO: Add other autocomplete parameters
                    // TODO: Support versions autocomplete.
                    // See: https://github.com/loic-sharma/BaGet/issues/291
                    var query = context.Request.ReadFromQuery("q");
                    var cancellationToken = context.RequestAborted;

                    var searchService = context.RequestServices.GetRequiredService<ISearchService>();

                    var response = await searchService.AutocompleteAsync(
                        query,
                        cancellationToken: cancellationToken);

                    await context.Response.WriteAsJsonAsync(response, cancellationToken);
                })
                .WithRouteName(Routes.AutocompleteRouteName);

            // This is an unofficial API to find packages that depend on a given package.
            endpoints
                .MapGet("v3/dependents", async context =>
                {
                    var packageId = context.Request.ReadFromQuery("packageId");
                    var cancellationToken = context.RequestAborted;

                    var searchService = context.RequestServices.GetRequiredService<ISearchService>();

                    var response = await searchService.FindDependentsAsync(
                        packageId,
                        cancellationToken: cancellationToken);

                    await context.Response.WriteAsJsonAsync(response, cancellationToken);
                })
                .WithRouteName(Routes.DependentsRouteName);
        }

        public void MapPackageMetadataRoutes(IEndpointRouteBuilder endpoints)
        {
            endpoints
                .MapGet("v3/registration/{id}/index.json", async context =>
                {
                    var packageId = context.Request.RouteValues["id"]?.ToString();
                    var cancellationToken = context.RequestAborted;

                    var metadata = context.RequestServices.GetRequiredService<IPackageMetadataService>();

                    var index = await metadata.GetRegistrationIndexOrNullAsync(packageId, cancellationToken);
                    if (index == null)
                    {
                        context.Response.NotFound();
                        return;
                    }

                    await context.Response.WriteAsJsonAsync(index, cancellationToken);
                })
                .WithRouteName(Routes.RegistrationIndexRouteName);

            endpoints
                .MapGet("v3/registration/{id}/{version}.json", async context =>
                {
                    var packageId = context.Request.RouteValues["id"]?.ToString();
                    var version = context.Request.RouteValues["version"]?.ToString();
                    var cancellationToken = context.RequestAborted;

                    var metadata = context.RequestServices.GetRequiredService<IPackageMetadataService>();

                    if (!NuGetVersion.TryParse(version, out var nugetVersion))
                    {
                        context.Response.NotFound();
                        return;
                    }

                    var leaf = await metadata.GetRegistrationLeafOrNullAsync(packageId, nugetVersion, cancellationToken);
                    if (leaf == null)
                    {
                        context.Response.NotFound();
                        return;
                    }

                    await context.Response.WriteAsJsonAsync(leaf, cancellationToken);
                })
                .WithRouteName(Routes.RegistrationLeafRouteName);
        }

        public void MapPackageContentRoutes(IEndpointRouteBuilder endpoints)
        {
            endpoints
                .MapGet("v3/package/{id}/index.json", async context =>
                {
                    var packageId = context.Request.RouteValues["id"]?.ToString();
                    var cancellationToken = context.RequestAborted;

                    var content = context.RequestServices.GetRequiredService<IPackageContentService>();
                    var response = await content.GetPackageVersionsOrNullAsync(packageId, cancellationToken);
                    if (response == null)
                    {
                        context.Response.NotFound();
                        return;
                    }

                    await context.Response.WriteAsJsonAsync(response, cancellationToken);
                })
                .WithRouteName(Routes.PackageVersionsRouteName);

            endpoints.MapControllerRoute(
                name: Routes.PackageDownloadRouteName,
                pattern: "v3/package/{id}/{version}/{idVersion}.nupkg",
                defaults: new { controller = "PackageContent", action = "DownloadPackage" });

            endpoints.MapControllerRoute(
                name: Routes.PackageDownloadManifestRouteName,
                pattern: "v3/package/{id}/{version}/{id2}.nuspec",
                defaults: new { controller = "PackageContent", action = "DownloadNuspec" });

            endpoints.MapControllerRoute(
                name: Routes.PackageDownloadReadmeRouteName,
                pattern: "v3/package/{id}/{version}/readme",
                defaults: new { controller = "PackageContent", action = "DownloadReadme" });

            endpoints.MapControllerRoute(
                name: Routes.PackageDownloadIconRouteName,
                pattern: "v3/package/{id}/{version}/icon",
                defaults: new { controller = "PackageContent", action = "DownloadIcon" });
        }
    }
}
