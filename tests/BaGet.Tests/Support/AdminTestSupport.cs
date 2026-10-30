using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>Seeding shared by the administrator integration tests.</summary>
internal static class AdminTestSupport
{
    public const string Password = "Passw0rd!";

    public static async Task CreateUserAsync(BaGetApplication app, string name, bool administrator = false, string? email = null)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var result = await users.CreateAsync(
            new BaGetUser
            {
                UserName = name,
                Email = email,
                EmailConfirmed = email != null,
                IsAdministrator = administrator,
                Created = DateTime.UtcNow,
            },
            Password);
        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    public static async Task SignInAsync(Browser browser, string name, string password = Password)
    {
        using var response = await browser.PostAsync("account/login", new Dictionary<string, string>
        {
            { "UserName", name },
            { "Password", password },
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public static async Task<bool> CanSignInAsync(BaGetApplication app, string name, string password)
    {
        using var browser = new Browser(app);
        using var response = await browser.PostAsync("account/login", new Dictionary<string, string>
        {
            { "UserName", name },
            { "Password", password },
        });

        #pragma warning disable CS8602
        return response.StatusCode == HttpStatusCode.Redirect
            && !response.Headers.Location.ToString().Contains("account/login", StringComparison.OrdinalIgnoreCase);
        #pragma warning restore CS8602
    }

    public static async Task<BaGetUser?> FindAsync(BaGetApplication app, string name)
    {
        using var scope = app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync(name);
    }

    /// <summary>Pushes a package as <paramref name="owner"/>, who becomes the ID's owner.</summary>
    public static async Task PushAsync(BaGetApplication app, string owner, string id, string version = "1.0.0")
    {
        var user = await FindAsync(app, owner);
        string key;
        using (var scope = app.Services.CreateScope())
        {
            #pragma warning disable CS8602
            key = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
                .CreateAsync(user.Id, "test", new[] { ApiKeyScopes.Push }, "*", null, CancellationToken.None);
            #pragma warning restore CS8602
        }

        using var client = app.CreateClient();
        using var package = TestPackages.Build(id, version);
        using var request = TestPackages.PushRequest(package, key);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public static async Task AddOrganizationAsync(BaGetApplication app, string name, params (string User, bool Admin)[] members)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var organization = new Organization { Name = name, Created = DateTime.UtcNow };
        #pragma warning disable CS8602
        context.Organizations.Add(organization);
        #pragma warning restore CS8602
        await context.SaveChangesAsync(CancellationToken.None);

        // One save per member, so the keys follow the order given.
        foreach (var (user, admin) in members)
        {
            var account = await users.FindByNameAsync(user);
            #pragma warning disable CS8602
            context.OrganizationMembers.Add(new OrganizationMember { OrganizationKey = organization.Key, UserId = account.Id, IsAdmin = admin });
            #pragma warning restore CS8602
            await context.SaveChangesAsync(CancellationToken.None);
        }
    }

    public static async Task AddOwnerAsync(BaGetApplication app, string packageId, string ownerName)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        #pragma warning disable CS8602
        context.PackageOwners.Add(new PackageOwner { PackageId = packageId, OwnerName = ownerName });
        #pragma warning restore CS8602
        await context.SaveChangesAsync(CancellationToken.None);
    }

    public static async Task<T> WithAsync<T>(BaGetApplication app, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = app.Services.CreateScope();

        return await action(scope.ServiceProvider);
    }

    public static Task<IReadOnlyList<PackageOwner>> OwnersAsync(BaGetApplication app, string? packageId)
        => WithAsync(app, p => p.GetRequiredService<PackageOwnershipService>().GetOwnersAsync(packageId, CancellationToken.None));

    public static Task<IReadOnlyList<Package>> VersionsAsync(BaGetApplication app, string? packageId)
        => WithAsync(app, p => p.GetRequiredService<IPackageDatabase>().FindAsync(packageId, includeUnlisted: true, CancellationToken.None));
}
