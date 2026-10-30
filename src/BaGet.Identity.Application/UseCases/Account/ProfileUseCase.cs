using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>
/// A user's or an organization's public page (<c>/profiles/{name}</c>, and under <c>/r/{repo}/</c> too): the packages it owns. It reveals only
/// what the page always showed (names, owned package IDs with their download counts, an avatar address), so an API key may read it as well as a session.
/// </summary>
public sealed class ProfileUseCase
{
    private readonly UserManager<BaGetUser> _users;
    private readonly PackageOwnershipService _ownership;
    private readonly IContext _context;
    private readonly AvatarService _avatars;

    /// <summary>Creates the use case.</summary>
    public ProfileUseCase(UserManager<BaGetUser> users, PackageOwnershipService ownership, IContext context, AvatarService avatars)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
    }

    /// <summary>The profile named <paramref name="name"/>; not found when neither a user nor an organization has it.</summary>
    public async Task<ScreenResult<ProfileViewModel>> ShowAsync(ClaimsPrincipal caller, string? name, CancellationToken cancellationToken)
    {
        var model = new ProfileViewModel { Name = name };
        #pragma warning disable CS8604
        var user = await _users.FindByNameAsync(model.Name);
        #pragma warning restore CS8604

        IReadOnlyList<string?> ids;
        if (user != null)
        {
            model.Name = user.UserName;
            model.IsCurrentUser = caller.Identity?.IsAuthenticated == true && _users.GetUserId(caller) == user.Id;

            // Only the packages owned in the user's own name: an organization's are on its page.
            #pragma warning disable CS8602
            var lowerName = user.UserName.ToLowerInvariant();
            #pragma warning disable CS8604
            ids = await _context.PackageOwners
                .AsNoTracking()
                .Where(o => o.OwnerName.ToLower() == lowerName)
                .Select(o => o.PackageId)
                .ToListAsync(cancellationToken);
            #pragma warning restore CS8602, CS8604
        }
        else
        {
            var members = await _ownership.GetMembersAsync(model.Name, cancellationToken);
            var lowerName = model.Name!.ToLowerInvariant();
            #pragma warning disable CS8602, CS8604
            var organization = await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Name.ToLower() == lowerName, cancellationToken);
            #pragma warning restore CS8602, CS8604
            if (organization == null) return ScreenResult<ProfileViewModel>.NotFound();

            model.Name = organization.Name;
            model.IsOrganization = true;

            var memberIds = members.Select(m => m.UserId).ToList();
            model.Members = await _context.Users
                .AsNoTracking()
                .Where(u => memberIds.Contains(u.Id))
                .Select(u => u.UserName)
                .OrderBy(n => n)
                .ToListAsync(cancellationToken);

            #pragma warning disable CS8602, CS8604
            ids = await _context.PackageOwners
                .AsNoTracking()
                .Where(o => o.OwnerName.ToLower() == lowerName)
                .Select(o => o.PackageId)
                .ToListAsync(cancellationToken);
            #pragma warning restore CS8602, CS8604
        }

        var avatars = await _avatars.GetUrlsAsync(new[] { model.Name! }, 64, cancellationToken);
        #pragma warning disable CS8620
        model.AvatarUrl = avatars.GetValueOrDefault(model.Name!);
        #pragma warning restore CS8620

        #pragma warning disable CS8602
        var lowerIds = ids.Select(i => i.ToLowerInvariant()).Distinct().ToList();
        #pragma warning disable CS8604
        model.Packages = await _context.Packages
            .AsNoTracking()
            .Where(p => lowerIds.Contains(p.Id.ToLower()))
            .GroupBy(p => p.Id)
            .Select(g => new PackageDownloads { Id = g.Key, Downloads = g.Sum(p => p.Downloads) })
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        return ScreenResult<ProfileViewModel>.Ok(model);
    }
}
