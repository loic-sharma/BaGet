using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using BaGet.Catalog.Controllers.Protocols;
using BaGet.Catalog.Extensions;
using BaGet.Core;
using BaGet.Frontend.Security;
using BaGet.Identity.Extensions;
using BaGet.Web.V3.OpenApi;
using BaGet.Web.V2.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Validation.Extensions;
using BaGet.Validation.Controllers.Protocols;
using BaGet.Web.Application.Extensions;
using BaGet.Web.V3.Controllers.Protocols;
using BaGet.Web.V3.Filters;
using BaGet.Web.V3.Oci;
using BaGet.Web.V3.Routing;
using BaGet.Web.V3.Workers;

namespace BaGet;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddBaGetWebApplication(
        this IServiceCollection services,
        Action<BaGetApplication> configureAction)
    {
        services
            .AddRouting(options => options.LowercaseUrls = true)
            .AddControllers(options =>
            {
                // The one read check of every repository page (404 when the caller may not see the repository): the controller
                // actions marked [RepositoryPage], and the /r/{repo}/ twin of their routes.
                options.Filters.Add<RepositoryActionFilter>();

                // Refuses a /r/{repo}/ request that never went through UseRepositoryResolution (else it would be served from `default`).
                options.Filters.Add<RepositoryResolutionGuardFilter>();
                options.Conventions.Add(new RouteNotForLinksConvention());
                options.Conventions.Add(new RepositoryActionRouteConvention());

                // The read check of every protocol route (401 for anonymous callers, 404 without Read access).
                options.Filters.Add<RepositoryReadFilter>();

                // A refusal by the vulnerability scanning policy is answered 403 with its reason, in the shape of the client's protocol.
                options.Filters.Add<ScanBlockedFilter>();

                // With nullable reference types on, MVC treats every non-nullable reference-type
                // property or parameter it binds as [Required]. Optional query values such as the
                // package list's "packageType" and "framework" would then fail validation whenever a
                // request leaves them out, and the page answer 400. Validation stays what the
                // attributes say it is; an explicit [Required] still applies.
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .AddBaGetApplicationParts()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        // The ui / v1 / protocols OpenAPI documents (served to administrators only, see BaGetEndpointBuilder.MapApiDocumentation).
        services.AddBaGetOpenApi();

        services.AddHttpContextAccessor();
        services.AddScoped<RequestPrincipalResolver>();

        // The use cases of the screens that moved to BaGet.Web.Application (the controllers call them through their JSON twins).
        services.AddBaGetWebUseCases();
        services.AddScoped<OciCallerResolver>();
        services.AddScoped<OciAuthorizationFilter>();
        services.AddTransient<IUrlGenerator, BaGetUrlGenerator>();

        services.AddBaGetApplication(configureAction);
        services.AddBaGetCatalog();
        services.AddBaGetValidation();

        // Browser uploads are staged between their two requests; whatever nobody comes
        // back to confirm or cancel is collected here.
        services.AddHostedService<StagingSweepWorker>();

        return services;
    }

    /// <summary>
    /// Register every BaGet assembly that holds controllers or Razor Pages. MVC otherwise finds
    /// them only through the [ApplicationPart] attributes the Web SDK generates into the entry
    /// assembly, which a host built with another SDK (a worker service, a test runner) lacks:
    /// accounts, the catalog, validation and the V2 feed would then answer 404.
    /// </summary>
    internal static IMvcBuilder AddBaGetApplicationParts(this IMvcBuilder builder)
        => builder.AddApplicationPartsOnce(
            typeof(PackageContentController).Assembly,
            typeof(V2FeedController).Assembly,
            typeof(CatalogController).Assembly,
            typeof(RepositorySignaturesController).Assembly,
            typeof(AccountsServiceCollectionExtensions).Assembly);

    /// <summary>
    /// Adds each of <paramref name="assemblies"/> as an MVC application part unless a part of that name exists already (the entry assembly
    /// may have contributed it); a part is added with its views, as a Razor SDK assembly's generated attributes would.
    /// </summary>
    internal static IMvcBuilder AddApplicationPartsOnce(this IMvcBuilder builder, params Assembly[] assemblies)
    {
        return builder.ConfigureApplicationPartManager(manager =>
        {
            foreach (var assembly in assemblies)
            {
                // The entry assembly may already have contributed it.
                var name = assembly.GetName().Name;
                if (manager.ApplicationParts.Any(part => part.Name == name)) continue;

                var factory = ApplicationPartFactory.GetApplicationPartFactory(assembly);
                foreach (var part in factory.GetApplicationParts(assembly))
                {
                    manager.ApplicationParts.Add(part);
                }
            }
        });
    }
}
