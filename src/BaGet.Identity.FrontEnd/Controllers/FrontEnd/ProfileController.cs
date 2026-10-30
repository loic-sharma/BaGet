using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Account;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// A user's or an organization's public page: the packages it owns (<c>/profiles/{name}</c>, and under
/// <c>/r/{repo}/</c> too). It replaces the Razor Page <c>Profiles/Profile</c>. The work is done by <see cref="ProfileApiController"/>.
/// </summary>
[RepositoryPage]
public sealed class ProfileController : AccountControllerBase
{
    /// <summary>The profile named <paramref name="name"/>; 404 when neither a user nor an organization has it.</summary>
    [Route("profiles/{name}"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Profile(string? name, CancellationToken cancellationToken)
        => Render(await Api<ProfileApiController, ProfileViewModel>(api => api.Profile(name, cancellationToken)), "Profile");
}
