using BaGet.Core;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Application.Services.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BaGet.Identity.Application.UseCases.Account;
using BaGet.Identity.Application.UseCases.Packages;

namespace BaGet.Identity.Application.Extensions;

/// <summary>Registration of what the Application project provides whether or not accounts are enabled.</summary>
public static class IdentityApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the screen gate. Called by <c>AddBaGetAccounts</c> (which stays the entry point of a host); the account services themselves are
    /// registered there only while accounts are enabled.
    /// </summary>
    public static IServiceCollection AddBaGetIdentityApplication(this IServiceCollection services)
    {
        services.TryAddScoped<ScreenGate>();

        return services;
    }

    /// <summary>
    /// Registers the use cases of the account and administration screens that exist only while accounts are enabled (they need the user store).
    /// Called by <c>AddBaGetAccounts</c> after its <c>Enabled</c> check, together with the host's <see cref="IReauthenticationState"/>.
    /// </summary>
    public static IServiceCollection AddBaGetIdentityUseCases(this IServiceCollection services)
    {
        services.TryAddSingleton(OpenIdConnectProviderSet.Empty);
        services.TryAddScoped<ExternalLogins>();
        services.TryAddScoped<ExternalProviderMapping>();
        services.TryAddScoped<PackageScreenServices>();
        services.TryAddScoped<PackageManageUseCase>();
        services.TryAddScoped<NpmManageUseCase>();
        services.TryAddScoped<MavenManageUseCase>();
        services.TryAddScoped<DockerManageUseCase>();
        services.TryAddScoped<ContactOwnersUseCase>();
        services.TryAddScoped<ValidationStatusUseCase>();
        services.TryAddScoped<ScanArtifactUseCase>();

        return services;
    }
}
