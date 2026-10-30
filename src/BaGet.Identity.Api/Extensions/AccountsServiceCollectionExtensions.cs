using System;
using BaGet.Core;
using BaGet.Frontend.Mvc.Screens;
using BaGet.Frontend.Mvc.UI;
using BaGet.Frontend.Security;
using BaGet.Identity.Application.Extensions;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BaGet.Identity.Application.Email;
using BaGet.Identity.Application.RateLimiting;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;
using BaGet.Identity.Application.Services.Sessions;
using BaGet.Identity.Application.UseCases.Account;

namespace BaGet.Identity.Extensions;

public static class AccountsServiceCollectionExtensions
{
    public const string OpenIdConnectScheme = ExternalSignInUseCase.OpenIdConnectScheme;

    /// <summary>
    /// Add accounts if "Accounts:Enabled" is set. Otherwise only an empty authentication
    /// setup is added, so the pipeline's authentication middleware has nothing to do.
    /// </summary>
    public static IServiceCollection AddBaGetAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(nameof(BaGetOptions.Accounts)).Get<AccountsOptions>()
                      ?? new AccountsOptions();

        var site = configuration.GetSection(nameof(BaGetOptions.Site)).Get<SiteOptions>() ?? new SiteOptions();
        EmailLinkPolicy.ValidatePublicUrl(site.PublicUrl);

        // The screen gate is needed whether or not accounts are enabled: the screens answer 404 through it while they are off.
        services.AddBaGetIdentityApplication();

        if (!options.Enabled)
        {
            services.AddAuthentication();
            return services;
        }

        // The providers whose handlers are registered below are the ones every screen and flow offers. An invalid configuration stops the startup.
        var providers = OpenIdConnectProviders.Resolve(options);
        services.Replace(ServiceDescriptor.Singleton(providers));

        services.AddHttpContextAccessor();
        services.TryAddScoped<IReauthenticationState, HttpReauthenticationState>();
        services.AddBaGetIdentityUseCases();

        services.TryAddScoped<ApiKeyService>();
        services.TryAddScoped<INpmTokenService, NpmTokenService>();
        services.TryAddScoped<PackageOwnershipService>();
        services.TryAddScoped<SessionPrincipalFactory>();
        services.TryAddScoped<ISessionPrincipalFactory>(provider => provider.GetRequiredService<SessionPrincipalFactory>());
        services.TryAddTransient<ApiKeyAuthenticationService>();
        services.TryAddScoped<AccountRecoveryService>();
        services.TryAddScoped<AccountDeletionService>();
        services.TryAddScoped<PasswordResetLinkSender>();
        services.TryAddScoped<AvatarService>();
        services.TryAddScoped<AdministratorService>();
        services.TryAddScoped<AccountApprovalService>();

        services.AddHttpContextAccessor();

        // The use cases of the account screens: resolved only after the screen gate let the request in, so they exist only while accounts are enabled.
        services.TryAddScoped<SignInUseCase>();
        services.TryAddScoped<RegistrationUseCase>();
        services.TryAddScoped<ExternalSignInUseCase>();
        services.TryAddScoped<AccountSettingsUseCase>();
        services.TryAddScoped<TwoFactorUseCase>();

        // Emails link to the single-page application's routes when the screens are off (or Frontend:SpaLinks): the sign-in page of the cookie is then the application's.
        var frontend = configuration.GetSection(nameof(BaGetOptions.Frontend)).Get<FrontendOptions>() ?? new FrontendOptions();
        var spaLinkBase = frontend.EmailLinkBase;
        if (frontend.EmailLinksWouldBeDead && (configuration.GetSection(nameof(BaGetOptions.Email)).Get<EmailOptions>()?.IsConfigured ?? false))
        {
            throw new InvalidOperationException(
                "'Frontend:Mvc' is false and email is configured, but account emails have no page to link to: set 'Frontend:SpaPath' (an application BaGet serves), 'Frontend:SpaUrl' (one hosted elsewhere) or 'Frontend:SpaLinks'=true (one a reverse proxy serves under 'Frontend:SpaBasePath').");
        }

        services.PostConfigure<SiteOptions>(site => site.SpaLinkBase = spaLinkBase);
        services.TryAddSingleton<ExchangeTokenLedger>();
        services.TryAddScoped<SessionTokenService>();
        services.TryAddScoped<SessionAuthenticator>();
        services.TryAddScoped<SessionUseCase>();
        services.TryAddScoped<ExternalSessionUseCase>();
        services.TryAddScoped<ApiKeysUseCase>();
        services.TryAddScoped<AccountOverviewUseCase>();
        services.TryAddScoped<ProfileUseCase>();
        services.TryAddScoped<AdminUserCreationService>();
        services.TryAddScoped<IScreenLinks, ScreenLinks>();
        services.AddBaGetAdminUseCases();
        services.TryAddSingleton<ContactRateLimiter>();
        services.TryAddSingleton<PasswordResetRateLimiter>();
        services.TryAddSingleton<BackgroundEmailQueue>();
        services.AddHostedService<BackgroundEmailService>();

        // Email is optional. Nothing that needs it is offered while it is unconfigured, and
        // Identity only demands a confirmed address when there is a way to send the link.
        var email = configuration.GetSection(nameof(BaGetOptions.Email)).Get<EmailOptions>()
                    ?? new EmailOptions();
        // Links in emails need a trustworthy address for this server. Without one, email is
        // switched off rather than built from the request's Host header.
        var linksAvailable = EmailLinkPolicy.CanBuildLinks(site.PublicUrl);
        if (email.IsConfigured && !linksAvailable) email.Suppressed = true;
        services.PostConfigure<EmailOptions>(configured => configured.Suppressed = email.Suppressed);

        if (email.IsConfigured)
        {
            services.TryAddTransient<IEmailSender<BaGetUser>, SmtpEmailSender>();
            services.TryAddTransient<IEmailMessageSender, SmtpEmailSender>();
        }
        else
        {
            services.TryAddTransient<IEmailSender<BaGetUser>, NoOpEmailSender>();
            services.TryAddTransient<IEmailMessageSender, NoOpEmailSender>();
        }
        services.Replace(ServiceDescriptor.Transient<Core.IAuthenticationService, AccountAuthenticationService>());
        services.Replace(ServiceDescriptor.Transient<IPackageAuthorizationService, OwnershipAuthorizationService>());

        // The context is chosen per database provider at runtime, so Identity's EF stores
        // are given whichever one was resolved rather than a context type.
        services.TryAddScoped<IUserStore<BaGetUser>>(provider =>
            new UserStore<BaGetUser, IdentityRole, DbContext, string>(
                (DbContext)provider.GetRequiredService<IContext>()));

        services
            .AddIdentityCore<BaGetUser>(identity =>
            {
                identity.User.RequireUniqueEmail = false;
                identity.Lockout.AllowedForNewUsers = true;
                identity.SignIn.RequireConfirmedEmail = email.IsConfigured;
            })
            .AddSignInManager<MaintenanceAwareSignInManager>()
            .AddDefaultTokenProviders();

        // Identity re-checks a session against the database only every 30 minutes by default,
        // so the session of a deleted user, or of one whose password was reset, would keep
        // acting until then. Browser sessions are few next to API traffic, so every request
        // is checked: no principal for a user that no longer exists or whose stamp changed.
        services.Configure<SecurityStampValidatorOptions>(validation => validation.ValidationInterval = TimeSpan.Zero);

        // The cookie session is accepted on /api/ui only together with this header (GET /api/ui/antiforgery hands the token out).
        services.AddAntiforgery(antiforgery => antiforgery.HeaderName = UiApiCaller.AntiforgeryHeader);

        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies(cookies => cookies.ApplicationCookie?.Configure(cookie =>
        {
            if (spaLinkBase != null && !spaLinkBase.StartsWith('/'))
            {
                // An application hosted elsewhere (an absolute address) cannot be a path: a challenge answers 401 instead of redirecting to a page of this server.
                cookie.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return System.Threading.Tasks.Task.CompletedTask; };
                cookie.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return System.Threading.Tasks.Task.CompletedTask; };

                return;
            }

            cookie.LoginPath = spaLinkBase == null ? "/account/login" : spaLinkBase + "/sign-in";
            cookie.LogoutPath = spaLinkBase == null ? "/account/logout" : spaLinkBase + "/sign-in";
            cookie.AccessDeniedPath = spaLinkBase == null ? "/account/login" : spaLinkBase + "/sign-in";
        }));

        // One authentication handler per provider: the legacy block as "oidc" (callback /signin-oidc), each named one under its own id and callback path.
        foreach (var provider in providers.All)
        {
            var configured = provider;
            authentication.AddOpenIdConnect(configured.Id, configured.DisplayName, oidc => OpenIdConnectProviderSetup.Configure(oidc, configured));
        }

        return services;
    }
}
