using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;

namespace BaGet.Extensions
{
    public static class IEndpointRouteBuilderExtensions
    {
        public static void MapServiceIndexRoutes(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapControllerRoute(
                name: Routes.IndexRouteName,
                pattern: "v3/index.json",
                defaults: new { controller = "ServiceIndex", action = "Get" });
        }

        public static void MapPackagePublishRoutes(this IEndpointRouteBuilder endpoints)
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

        public static void MapSymbolRoutes(this IEndpointRouteBuilder endpoints)
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

        public static void MapSearchRoutes(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapControllerRoute(
                name: Routes.SearchRouteName,
                pattern: "v3/search",
                defaults: new { controller = "Search", action = "Search" });

            endpoints.MapControllerRoute(
                name: Routes.AutocompleteRouteName,
                pattern: "v3/autocomplete",
                defaults: new { controller = "Search", action = "Autocomplete" });

            // This is an unofficial API to find packages that depend on a given package.
            endpoints.MapControllerRoute(
                name: Routes.DependentsRouteName,
                pattern: "v3/dependents",
                defaults: new { controller = "Search", action = "Dependents" });
        }

        public static void MapPackageMetadataRoutes(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapControllerRoute(
               name: Routes.RegistrationIndexRouteName,
               pattern: "v3/registration/{id}/index.json",
               defaults: new { controller = "PackageMetadata", action = "RegistrationIndex" });

            endpoints.MapControllerRoute(
                name: Routes.RegistrationLeafRouteName,
                pattern: "v3/registration/{id}/{version}.json",
                defaults: new { controller = "PackageMetadata", action = "RegistrationLeaf" });
        }

        public static void MapPackageContentRoutes(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapControllerRoute(
                name: Routes.PackageVersionsRouteName,
                pattern: "v3/package/{id}/index.json",
                defaults: new { controller = "PackageContent", action = "GetPackageVersions" });

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
        }
    }
}
