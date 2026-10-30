using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Protocol.Models;
using BaGet.Catalog.Controllers.Protocols;
using BaGet.Catalog.Database;

namespace BaGet.Catalog.ServiceIndex;

/// <summary>
/// Advertises Catalog/3.0.0, unless the database cannot hold the catalog or the repository is virtual.
/// </summary>
public class CatalogServiceIndexProvider : IServiceIndexResourceProvider
{
    private readonly ICatalogDatabase _catalog;
    private readonly IUrlGenerator _url;
    private readonly IRepositoryContext? _repository;

    public CatalogServiceIndexProvider(ICatalogDatabase catalog, IUrlGenerator url, IRepositoryContext? repository = null)
    {
        _repository = repository;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _url = url ?? throw new ArgumentNullException(nameof(url));
    }

    public IEnumerable<ServiceIndexItem> GetResources()
    {
        // A virtual repository has no catalog (CatalogController answers 404 there).
        if (!_catalog.IsSupported || _repository?.Current.Kind == RepositoryKind.Virtual) yield break;

        yield return new ServiceIndexItem
        {
            ResourceUrl = _url.GetCatalogIndexUrl(),
            Type = "Catalog/3.0.0",
        };
    }
}