using BaGet.Core.Server.Transformers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;

namespace BaGet.Extensions
{
    public static class IRouteBuilderExtensions
    {
        public static IEndpointRouteBuilder MapServiceIndexRoutes(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapDynamicControllerRoute<NugetServiceIndexRoutesTransformer>("v3/index.json");
            return endpoints;
        }

        public static IEndpointRouteBuilder MapPackagePublishRoutes(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapDynamicControllerRoute<NugetPackagePublishRoutesTransformer>("api/v2/package/{id?}/{version?}");
            return endpoints;
        }

        public static IEndpointRouteBuilder MapSymbolRoutes(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapDynamicControllerRoute<NugetSymbolRoutesTransformer>("api/v2/symbol");
            endpoints.MapDynamicControllerRoute<NugetSymbolRoutesTransformer>("api/v2/symbol/{first}/{second}/{thrid}/{forth?}");
            endpoints.MapDynamicControllerRoute<NugetSymbolRoutesTransformer>("api/v2/symbols/{first}/{second}/{thrid}/{forth?}");
            return endpoints;
            /*routes.MapRoute(
                name: Routes.UploadSymbolRouteName,
                template: "api/v2/symbol",
                defaults: new { controller = "Symbol", action = "Upload" },
                constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

            routes.MapRoute(
                name: Routes.SymbolDownloadRouteName,
                template: "api/download/symbols/{file}/{key}/{file2}",
                defaults: new { controller = "Symbol", action = "Get" });
            
            routes.MapRoute(
                name: Routes.SymbolDownloadRouteNamePrefix,
                template: "api/download/symbols/{prefix}/{file}/{key}/{file2}",
                defaults: new { controller = "Symbol", action = "Get" });

            return routes;*/
        }

        public static IEndpointRouteBuilder MapSearchRoutes(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapDynamicControllerRoute<NugetSearchRoutesTransformer>("v3/{action}");
            return endpoints;
        }

        public static IEndpointRouteBuilder MapPackageMetadataRoutes(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapDynamicControllerRoute<NugetPackageMetadataRoutesTransformer>("v3/registration/{id}/index.json");
            endpoints.MapDynamicControllerRoute<NugetPackageMetadataRoutesTransformer>("v3/registration/{id}/{version}.json");
            return endpoints;
            /*
            routes.MapRoute(
               name: Routes.RegistrationIndexRouteName,
               template: "v3/registration/{id}/index.json",
               defaults: new { controller = "PackageMetadata", action = "RegistrationIndexAsync" });

            routes.MapRoute(
                name: Routes.RegistrationLeafRouteName,
                template: "v3/registration/{id}/{version}.json",
                defaults: new { controller = "PackageMetadata", action = "RegistrationLeafAsync" });

            return routes;*/
        }

        public static IEndpointRouteBuilder MapPackageContentRoutes(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapDynamicControllerRoute<NugetPackageContentRoutesTransformer>("v3/package/{id}/{ext}");
            endpoints.MapDynamicControllerRoute<NugetPackageContentRoutesTransformer>("v3/package/{id}/{version}/{idVersion}.nupkg");
            endpoints.MapDynamicControllerRoute<NugetPackageContentRoutesTransformer>("v3/package/{id}/{version}/{id2}.nuspec");
            endpoints.MapDynamicControllerRoute<NugetPackageContentRoutesTransformer>("v3/package/{id}/{version}/readme");
            return endpoints;
            /*
            routes.MapRoute(
                name: Routes.PackageVersionsRouteName,
                template: "v3/package/{id}/index.json",
                defaults: new { controller = "PackageContent", action = "GetPackageVersionsAsync" });

            routes.MapRoute(
                name: Routes.PackageDownloadRouteName,
                template: "v3/package/{id}/{version}/{idVersion}.nupkg",
                defaults: new { controller = "PackageContent", action = "DownloadPackageAsync" });

            routes.MapRoute(
                name: Routes.PackageDownloadManifestRouteName,
                template: "v3/package/{id}/{version}/{id2}.nuspec",
                defaults: new { controller = "PackageContent", action = "DownloadNuspecAsync" });

            routes.MapRoute(
                name: Routes.PackageDownloadReadmeRouteName,
                template: "v3/package/{id}/{version}/readme",
                defaults: new { controller = "PackageContent", action = "DownloadReadmeAsync" });

            return routes;*/
        }
    }
}
