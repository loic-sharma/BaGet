using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BaGet.Identity.Application.UseCases.Access;
using BaGet.Identity.Application.UseCases.Admin;
using BaGet.Identity.Application.UseCases.Packages;
using BaGet.Identity.Application.UseCases.Policies;

namespace BaGet.Identity.Application.Extensions;

/// <summary>Registration of the use cases of the account and administration screens.</summary>
public static class AdminUseCasesServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PasswordConfirmation"/> and the use cases of the administration screens. Called by <c>AddBaGetAccounts</c> after its
    /// <c>Accounts:Enabled</c> check: the use cases depend on the account services, which exist only while accounts are enabled, so a JSON twin
    /// resolves its use case inside the action (after the screen gate), never in its constructor.
    /// </summary>
    public static IServiceCollection AddBaGetAdminUseCases(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<PasswordConfirmation>();

        services.TryAddScoped<AdminUsersUseCase>();
        services.TryAddScoped<AdminUserDetailUseCase>();
        services.TryAddScoped<AdminUserDeletionUseCase>();
        services.TryAddScoped<AdminPackagesUseCase>();
        services.TryAddScoped<AdminOrganizationsUseCase>();
        services.TryAddScoped<SignInProviderSettings>();
        services.TryAddScoped<AdminSettingsUseCase>();
        services.TryAddScoped<AdminOperationsUseCase>();
        services.TryAddScoped<AdminGroupsUseCase>();
        services.TryAddScoped<AdminPermissionsUseCase>();
        services.TryAddScoped<AdminProjectsUseCase>();
        services.TryAddScoped<AdminAuditUseCase>();
        services.TryAddScoped<RepositoryFeedUrl>();
        services.TryAddScoped<AdminRepositoriesUseCase>();
        services.TryAddScoped<AdminRepositoryEditUseCase>();
        services.TryAddScoped<AdminRepositoryDeleteUseCase>();
        services.TryAddScoped<RetentionUseCase>();
        services.TryAddScoped<ScanningUseCase>();
        services.TryAddScoped<ReplicationUseCase>();

        return services;
    }
}
