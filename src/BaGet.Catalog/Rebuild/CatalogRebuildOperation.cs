using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Catalog.Database;

namespace BaGet.Catalog.Rebuild;

/// <summary>The <c>catalog rebuild</c> command as an <see cref="IAdminOperation"/>.</summary>
public sealed class CatalogRebuildOperation : IAdminOperation
{
    public string Name => "catalog-rebuild";

    public string Title => "Rebuild the catalog";

    public string Description => "Appends the catalog entries missing for the repository's packages (backfills packages pushed before the catalog existed, repairs failed commits).";

    public bool IsAvailable(IServiceProvider services)
        => services.GetRequiredService<ICatalogDatabase>().IsSupported;

    public async Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var (added, deleted) = await services.GetRequiredService<CatalogRebuilder>().RebuildAsync(cancellationToken).ConfigureAwait(false);

        return $"{added} details entries and {deleted} delete entries appended.";
    }
}
