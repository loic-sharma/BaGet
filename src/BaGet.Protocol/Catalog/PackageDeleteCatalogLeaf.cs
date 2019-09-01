namespace BaGet.Protocol
{
    /// <summary>
    /// A "package delete" catalog leaf. Represents a single package deletion event.
    /// Leafs can be discovered from a <see cref="CatalogPage"/>.
    /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-leaf
    /// </summary>
    public class PackageDeleteCatalogLeaf : CatalogLeaf
    {
    }
}
