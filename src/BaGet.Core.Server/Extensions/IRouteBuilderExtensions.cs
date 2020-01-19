using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;

namespace BaGet.Extensions
{
    public static class IRouteBuilderExtensions
    {
        public static IRouteBuilder MapServiceIndexRoutes(this IRouteBuilder routes)
        {
            return routes.MapRoute(
                name: Routes.IndexRouteName,
                template: "v3/index.json",
                defaults: new { controller = "ServiceIndex", action = "Get" });
        }

        public static IRouteBuilder MapPackagePublishRoutes(this IRouteBuilder routes)
        {
            routes.MapRoute(
                name: Routes.UploadPackageRouteName,
                template: "api/v2/package",
                defaults: new { controller = "PackagePublish", action = "Upload" },
                constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

            routes.MapRoute(
                name: Routes.DeleteRouteName,
                template: "api/v2/package/{id}/{version}",
                defaults: new { controller = "PackagePublish", action = "Delete" },
                constraints: new { httpMethod = new HttpMethodRouteConstraint("DELETE") });

            routes.MapRoute(
                name: Routes.RelistRouteName,
                template: "api/v2/package/{id}/{version}",
                defaults: new { controller = "PackagePublish", action = "Relist" },
                constraints: new { httpMethod = new HttpMethodRouteConstraint("POST") });

            return routes;
        }

        public static IRouteBuilder MapSymbolRoutes(this IRouteBuilder routes)
        {
            routes.MapRoute(
                name: Routes.UploadSymbolRouteName,
                template: "api/v2/symbol",
                defaults: new { controller = "Symbol", action = "Upload" },
                constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

            routes.MapRoute(
                name: Routes.SymbolDownloadRouteName,
                template: "api/download/symbols/{file}/{key}/{file2}",
                defaults: new { controller = "Symbol", action = "Get" });

            routes.MapRoute(
                name: Routes.SymbolDownloadPrefixRouteName,
                template: "api/download/symbols/{prefix}/{file}/{key}/{file2}",
                defaults: new { controller = "Symbol", action = "Get" });

            return routes;
        }

        public static IRouteBuilder MapSearchRoutes(this IRouteBuilder routes)
        {
            routes.MapRoute(
                name: Routes.SearchRouteName,
                template: "v3/search",
                defaults: new { controller = "Search", action = "Search" });

            routes.MapRoute(
                name: Routes.AutocompleteRouteName,
                template: "v3/autocomplete",
                defaults: new { controller = "Search", action = "Autocomplete" });

            // This is an unofficial API to find packages that depend on a given package.
            routes.MapRoute(
                name: Routes.DependentsRouteName,
                template: "v3/dependents",
                defaults: new { controller = "Search", action = "Dependents" });

            return routes;
        }

        public static IRouteBuilder MapPackageMetadataRoutes(this IRouteBuilder routes)
        {
            routes.MapRoute(
               name: Routes.RegistrationIndexRouteName,
               template: "v3/registration/{id}/index.json",
               defaults: new { controller = "PackageMetadata", action = "RegistrationIndex" });

            routes.MapRoute(
                name: Routes.RegistrationLeafRouteName,
                template: "v3/registration/{id}/{version}.json",
                defaults: new { controller = "PackageMetadata", action = "RegistrationLeaf" });

            return routes;
        }

        public static IRouteBuilder MapPackageContentRoutes(this IRouteBuilder routes)
        {
            routes.MapRoute(
                name: Routes.PackageVersionsRouteName,
                template: "v3/package/{id}/index.json",
                defaults: new { controller = "PackageContent", action = "GetPackageVersions" });

            routes.MapRoute(
                name: Routes.PackageDownloadRouteName,
                template: "v3/package/{id}/{version}/{idVersion}.nupkg",
                defaults: new { controller = "PackageContent", action = "DownloadPackage" });

            routes.MapRoute(
                name: Routes.PackageDownloadManifestRouteName,
                template: "v3/package/{id}/{version}/{id2}.nuspec",
                defaults: new { controller = "PackageContent", action = "DownloadNuspec" });

            routes.MapRoute(
                name: Routes.PackageDownloadReadmeRouteName,
                template: "v3/package/{id}/{version}/readme",
                defaults: new { controller = "PackageContent", action = "DownloadReadme" });

            return routes;
        }
    }
}
