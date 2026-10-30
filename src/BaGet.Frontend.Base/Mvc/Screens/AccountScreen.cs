using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Identity.Application.Email;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Frontend.Mvc.Screens;

/// <summary>
/// What an account workflow needs from the request, whatever hosts it (<see cref="AccountControllerBase"/>). The services are resolved per call: they are
/// only registered while accounts are enabled, and the callers check that first.
/// </summary>
internal sealed class AccountScreen
{
    public AccountScreen(HttpContext httpContext, IUrlHelper url, ModelStateDictionary modelState)
    {
        HttpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));
        Url = url ?? throw new ArgumentNullException(nameof(url));
        ModelState = modelState ?? throw new ArgumentNullException(nameof(modelState));
    }

    public HttpContext HttpContext { get; }

    public IUrlHelper Url { get; }

    public ModelStateDictionary ModelState { get; }

    public ClaimsPrincipal User => HttpContext.User;

    public IServiceProvider Services => HttpContext.RequestServices;

    public AccountsOptions Accounts => Services.GetRequiredService<IOptions<AccountsOptions>>().Value;

    public SignInManager<BaGetUser> SignInManager => Services.GetRequiredService<SignInManager<BaGetUser>>();

    public UserManager<BaGetUser> UserManager => Services.GetRequiredService<UserManager<BaGetUser>>();

    public PackageOwnershipService Ownership => Services.GetRequiredService<PackageOwnershipService>();

    public ApiKeyService ApiKeys => Services.GetRequiredService<ApiKeyService>();

    public bool EmailConfigured => Services.GetRequiredService<IOptions<EmailOptions>>().Value.IsConfigured;

    public IEmailSender<BaGetUser> EmailSender => Services.GetRequiredService<IEmailSender<BaGetUser>>();

    public DateTime UtcNow => Services.GetRequiredService<SystemTime>().UtcNow;

    public string AbsoluteLink(ScreenRef screen, object values) => AccountSupport.AbsoluteLink(HttpContext, Url, screen, values);

    public Task<bool> IsConfirmedAsync(BaGetUser? user, string? password)
        => AccountSupport.IsConfirmedAsync(UserManager, HttpContext, user, password);

    public Task<bool> HasReauthenticatedRecentlyAsync() => AccountSupport.HasReauthenticatedRecentlyAsync(HttpContext);

    public static string Describe(IdentityResult result) => AccountSupport.Describe(result);

    public static string EncodeToken(string token) => PasswordResetLinkSender.EncodeToken(token);

    public static string? DecodeToken(string? code) => AccountSupport.DecodeToken(code);
}
