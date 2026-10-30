using BaGet.Identity.Application.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BaGet.Web.Application.Models.Packages;
using BaGet.Web.Application.Services;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.Application.Extensions;

/// <summary>Registration of the use cases of the web screens.</summary>
public static class WebApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the use cases (one scoped class per area of screens) and the account project's screen gate. Called by
    /// <c>AddBaGetWebApplication</c>; a track that moves a screen's logic here adds its use case below.
    /// </summary>
    public static IServiceCollection AddBaGetWebUseCases(this IServiceCollection services)
    {
        services.AddBaGetIdentityApplication();

        services.TryAddScoped<LegalUseCase>();
        services.TryAddScoped<StatisticsUseCase>();
        services.TryAddScoped<IndexUseCase>();
        services.TryAddScoped<PackageUseCase>();
        services.TryAddScoped<ReportAbuseUseCase>();

        // The format screens and the browser uploads.
        services.TryAddScoped<UploadAuthenticator>();
        services.TryAddScoped<ManageAccess>();
        services.TryAddScoped<UploadUseCase>();
        services.TryAddScoped<NpmBrowseUseCase>();
        services.TryAddScoped<MavenBrowseUseCase>();
        services.TryAddScoped<DockerBrowseUseCase>();

        return services;
    }
}
