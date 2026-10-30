using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.Models.Api;
using BaGet.Web.V3.Api;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Controllers.Api;

/// <summary>
/// Accounts: the Users and UserDetail pages' operations (list, create, promote and demote, group membership, revoking a key). Password
/// resets and account deletion are not offered over the API: they mail or destroy on someone's behalf and stay on the pages. Needs
/// <c>Accounts:Enabled</c>; without it every route answers 404.
/// </summary>
[Route(ApiCatalog.RootTemplate + "/users")]
public class UsersController : AdminApiController
{
    private AdministratorService? Administrators => HttpContext.RequestServices.GetService<AdministratorService>();

    private UserManager<BaGetUser> Users => Service<UserManager<BaGetUser>>();

    private IActionResult? AccountsDisabled()
        => Administrators == null ? ApiProblems.NotFound("user accounts here: the server runs with Accounts:Enabled off") : null;

    /// <summary>Lists users.</summary>
    [HttpGet]
    [ApiOperation("List users, optionally by a name fragment", "AdminUsers", "Index", Response = typeof(UserDto), PagedList = true)]
    public async Task<IActionResult> List(string? q, int? page, int? pageSize)
    {
        if (AccountsDisabled() is { } disabled) return disabled;

        var query = Service<IContext>().Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            #pragma warning disable CS8602
            query = query.Where(u => u.UserName.ToLower().Contains(term));
            #pragma warning restore CS8602
        }

        var total = await query.CountAsync(Cancellation);
        var (number, size) = ApiProblems.Clamp(page, pageSize);
        var rows = await query.OrderBy(u => u.UserName).Skip((number - 1) * size).Take(size).ToListAsync(Cancellation);

        return Respond(new Paged<UserDto>(rows.Select(ToDto).ToList(), number, size, total));
    }

    /// <summary>One user.</summary>
    [HttpGet("{name}")]
    [ApiOperation("Show a user with groups, project roles, organizations and API keys", "AdminUsers", "Detail", Response = typeof(UserDetailDto))]
    public async Task<IActionResult> Get(string name)
    {
        if (AccountsDisabled() is { } disabled) return disabled;
        var user = await Users.FindByNameAsync(name);
        if (user == null) return ApiProblems.NotFound($"user '{name}'");

        var groups = new List<string>();
        var roles = new List<UserProjectRole>();
        if (Service<IContext>() is not NullContext)
        {
            var access = Service<IAccessAdministration>();
            groups = (await access.GroupsOfUserAsync(user.Id, Cancellation)).ToList();
            roles = (await access.ProjectRolesOfUserAsync(user.Id, Cancellation)).ToList();
        }

        var keysService = Service<ApiKeyService>();
        var keys = await keysService.ListAsync(user.Id, Cancellation);
        var repositories = await keysService.RepositoryNamesAsync(keys, Cancellation);
        var ownership = Service<PackageOwnershipService>();
        var organizations = (await ownership.GetOrganizationsAsync(user.Id, Cancellation))?.Select(o => o.Name ?? string.Empty).ToList() ?? new List<string>();

        return Respond(new UserDetailDto(
            ToDto(user),
            groups,
            roles,
            organizations,
            await ownership.CountOwnedPackageIdsAsync(user.UserName, Cancellation),
            keys.Select(k => new ApiKeyDto(
                k.Key, k.Description, k.Scopes ?? Array.Empty<string>(), k.PackageGlobPattern, k.Created, k.Expires,
                ApiKeyService.DescribeRepositories(repositories.GetValueOrDefault(k.Key)))).ToList()));
    }

    /// <summary>What the user may do in each repository.</summary>
    [HttpGet("{name}/access")]
    [ApiOperation("Show what a user may do in each repository, and the grants behind it (for one package id with ?package=)", "AdminUsers", "Detail", Response = typeof(UserAccessDto))]
    public async Task<IActionResult> Access(string name, string? package)
    {
        if (AccountsDisabled() is { } disabled) return disabled;
        var user = await Users.FindByNameAsync(name);
        if (user == null) return ApiProblems.NotFound($"user '{name}'");
        if (Service<IContext>() is NullContext) return ApiProblems.Create(422, "not-supported", "Not supported here", "This database has no access model.");

        var evaluator = Service<IAccessEvaluator>();
        var id = string.IsNullOrWhiteSpace(package) ? null : package.Trim();
        var principal = new BaGetPrincipal(user.Id, user.UserName, isSuperuser: false, ApiKeyScopes.All, "*", user.IsAdministrator);
        var rows = new List<UserAccessDto>();
        foreach (var repository in (await Service<IRepositoryService>().ListAsync(Cancellation)).OrderBy(r => r.IsDefault ? 0 : 1).ThenBy(r => r.Name, StringComparer.Ordinal))
        {
            var explanation = await evaluator.ExplainAsync(principal, repository, id, Cancellation);
            rows.Add(new UserAccessDto(repository.Name, explanation.Actions, explanation.Override, explanation.Reasons));
        }

        return Respond(rows);
    }

    /// <summary>Creates an account.</summary>
    [HttpPost]
    [ApiOperation("Create a user (with a given or a generated password, returned once)", "AdminUsers", "Index", Confirm = true, Response = typeof(CreatedUserDto), Status = 201)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        if (AccountsDisabled() is { } disabled) return disabled;
        ThrowIfClosed();
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        var mode = string.IsNullOrEmpty(request.Password) ? NewUserPasswordMode.Generated : NewUserPasswordMode.Given;
        var created = await Service<AdminUserCreationService>().CreateAsync(request.UserName, request.Email, mode, request.Password, Cancellation);
        if (!created.Succeeded) return ApiProblems.BadRequest(created.Error ?? "The user was not created.");

        Response.Headers.CacheControl = "no-store";

        return Respond(new CreatedUserDto(ToDto(created.User!), created.GeneratedPassword), 201, $"{ApiCatalog.Root}/users/{Uri.EscapeDataString(created.User!.UserName!)}");
    }

    /// <summary>Promotes or demotes a user.</summary>
    [HttpPut("{name}/administrator")]
    [ApiOperation("Make a user an administrator or take the flag away (never the last administrator)", "AdminUsers", "Index", Confirm = true, Response = typeof(UserDto))]
    public async Task<IActionResult> SetAdministrator(string name, [FromBody] AdministratorRequest request)
    {
        if (AccountsDisabled() is { } disabled) return disabled;
        ThrowIfClosed();
        if (request.IsAdministrator == null) return ApiProblems.BadRequest("'isAdministrator' is required.");
        if (await Users.FindByNameAsync(name) == null) return ApiProblems.NotFound($"user '{name}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        var administrators = Administrators!;
        var change = request.IsAdministrator.Value
            ? await administrators.PromoteAsync(name, Actor, Cancellation)
            : await administrators.DemoteAsync(name, Actor, Cancellation);
        if (change == AdministratorChange.LastAdministrator) return ApiProblems.Conflict("The last administrator cannot be demoted. Promote another user first.");

        return Respond(ToDto((await Users.FindByNameAsync(name))!));
    }

    /// <summary>Adds the user to a group.</summary>
    [HttpPut("{name}/groups/{group}")]
    [ApiOperation("Add a user to a group", "AdminUsers", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> AddGroup(string name, string group)
    {
        if (AccountsDisabled() is { } disabled) return disabled;
        ThrowIfClosed();
        var user = await Users.FindByNameAsync(name);
        if (user == null || Service<IContext>() is NullContext) return ApiProblems.NotFound($"user '{name}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await Service<IAccessAdministration>().AddGroupMemberAsync(group, user.Id, Actor, Cancellation);

        return Done();
    }

    /// <summary>Removes the user from a group.</summary>
    [HttpDelete("{name}/groups/{group}")]
    [ApiOperation("Remove a user from a group", "AdminUsers", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> RemoveGroup(string name, string group)
    {
        if (AccountsDisabled() is { } disabled) return disabled;
        ThrowIfClosed();
        var user = await Users.FindByNameAsync(name);
        if (user == null || Service<IContext>() is NullContext) return ApiProblems.NotFound($"user '{name}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await Service<IAccessAdministration>().RemoveGroupMemberAsync(group, user.Id, Actor, Cancellation);

        return Done();
    }

    /// <summary>Revokes one of the user's keys.</summary>
    [HttpDelete("{name}/keys/{key:int}")]
    [ApiOperation("Revoke one of a user's API keys", "AdminUsers", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> RevokeKey(string name, int key)
    {
        if (AccountsDisabled() is { } disabled) return disabled;
        ThrowIfClosed();
        var user = await Users.FindByNameAsync(name);
        if (user == null) return ApiProblems.NotFound($"user '{name}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        if (!await Service<ApiKeyService>().RevokeAsync(user.Id, key, Cancellation)) return ApiProblems.NotFound($"API key {key} of '{name}'");

        return Done();
    }

    private static UserDto ToDto(BaGetUser user) => new(user.UserName ?? string.Empty, user.Email, user.Created, user.IsAdministrator, user.PendingApproval);
}
