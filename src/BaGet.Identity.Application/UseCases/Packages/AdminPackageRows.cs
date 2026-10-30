using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using BaGet.Identity.Application.Models.Packages;

namespace BaGet.Identity.Application.UseCases.Packages;

internal static class AdminPackageRows
{
    /// <summary>
    /// Summarises <paramref name="ids"/> and attaches each one's owners: one query for the versions, one for the owners.
    /// Works in the repository <paramref name="context"/> is scoped to, whose name rows carry.
    /// </summary>
    internal static async Task<IReadOnlyList<AdminPackageRow>> BuildAsync(
        IContext context,
        IReadOnlyList<string>? ids,
        CancellationToken cancellationToken,
        string repositoryName = Repository.DefaultName)
    {
        #pragma warning disable CS8602
        if (ids.Count == 0) return Array.Empty<AdminPackageRow>();
        #pragma warning restore CS8602

        var summaries = PackagesModel.Summarise(
            ids,
            await PackagesModel.ListVersionsAsync(context, ids, cancellationToken));

        var lowered = ids.Select(id => id.ToLowerInvariant()).ToList();
        #pragma warning disable CS8602, CS8604
        var owners = await context.PackageOwners
            .AsNoTracking()
            .Where(o => lowered.Contains(o.PackageId.ToLower()))
            .Select(o => new { o.PackageId, o.OwnerName })
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604
        #pragma warning disable CS8621, CS8714
        var byId = owners
            .GroupBy(o => o.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(o => o.OwnerName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);
        #pragma warning restore CS8621, CS8714

        return summaries
            .Select(p => new AdminPackageRow(p, byId.TryGetValue(p.Id, out var names) ? names : Array.Empty<string>(), repositoryName))
            .ToList();
    }
}
