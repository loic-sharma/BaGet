using BaGet.Frontend.Routing;
using BaGet.Identity.Controllers.FrontEnd;
using BaGet.Web.V3.Controllers.FrontEnd;
using BaGet.Web.V3.Extensions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BaGet;

/// <summary>Registers BaGet's web UI: the view controllers and views, as opposed to the web APIs <c>AddBaGetWebApplication</c> registers.</summary>
public static class FrontEndServiceCollectionExtensions
{
    /// <summary>
    /// Adds the screens of the web UI to a host that called <c>AddBaGetWebApplication</c>: the three front-end assemblies
    /// (<c>BaGet.Frontend.Base</c> with the layout and shared partials, <c>BaGet.Identity.FrontEnd</c> and <c>BaGet.Web.FrontEnd</c>) as MVC application
    /// parts (a host without the Web SDK has no generated <c>[ApplicationPart]</c> attributes), Razor view support, and the link helpers that keep
    /// a page's links inside the repository being browsed. A host that serves only the APIs does not call it and has no screen.
    /// </summary>
    public static IServiceCollection AddBaGetFrontEnd(this IServiceCollection services)
    {
        // BaGet ships no Razor Page (every screen is a controller action), but a host's own pages (which find BaGet's layout in Views/Shared) keep working, so Razor Pages stays registered.
        services.AddRazorPages();

        services
            .AddControllers()
            .AddApplicationPartsOnce(
                typeof(RepositoryUrlHelper).Assembly,
                typeof(AdminUsersController).Assembly,
                typeof(HomeController).Assembly);

        // Page links stay inside the repository being browsed (see RepositoryUrlHelper).
        services.Replace(ServiceDescriptor.Singleton<IUrlHelperFactory, RepositoryUrlHelperFactory>());

        // Which controller action is a repository page, so a link to it carries the request's repository.
        services.TryAddSingleton<UiActionMap>();

        return services;
    }
}
