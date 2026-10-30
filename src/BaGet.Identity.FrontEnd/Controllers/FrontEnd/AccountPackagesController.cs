using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.Models.Packages;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The signed-in user's own packages and permissions: "My packages", "My access" and the redirect to the user's profile. It
/// replaces the Razor Pages <c>Account/Packages</c>, <c>Account/Access</c> and <c>Account/Profile</c>: same URLs. Every action needs a signed-in user.
/// The work is done by <see cref="AccountPackagesApiController"/>.
/// </summary>
public sealed class AccountPackagesController : AccountControllerBase
{
    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    /// <summary>The package IDs the user owns, per repository.</summary>
    [Route("account/packages"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Packages(CancellationToken cancellationToken)
        => Render(await Api<AccountPackagesApiController, AccountPackagesViewModel>(api => api.Packages(cancellationToken)), "Packages");

    /// <summary>What the user may do in each repository.</summary>
    [Route("account/access"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Access(string? package, CancellationToken cancellationToken)
        => Render(await Api<AccountPackagesApiController, AccountAccessViewModel>(api => api.Access(package, cancellationToken)), "Access");

    /// <summary>The signed-in user's own profile: forwards to <c>/profiles/{name}</c>.</summary>
    [Route("account/profile"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> MyProfile()
        => Render(await Api<AccountPackagesApiController, EmptyScreen>(api => api.MyProfile()), "MyProfile", _ => null);
}
