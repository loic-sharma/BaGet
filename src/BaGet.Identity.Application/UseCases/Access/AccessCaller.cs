using System.Security.Claims;

namespace BaGet.Identity.Application.UseCases.Access;

/// <summary>
/// Who is using an access-control screen: the signed-in account (or the key owner's) and whether it is a server administrator rather than only a
/// project administrator. The screen gate decided that before the use case runs; the use case only needs it for the rules inside a screen.
/// </summary>
/// <param name="Principal">The signed-in user (for an API key, the key's owner).</param>
/// <param name="IsServerAdministrator">Whether the caller is a server administrator.</param>
public sealed record AccessCaller(ClaimsPrincipal Principal, bool IsServerAdministrator);
