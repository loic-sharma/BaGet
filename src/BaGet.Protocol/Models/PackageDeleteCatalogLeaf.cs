namespace BaGet.Protocol.Models
{
    // This class is based off https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Models/PackageDeleteCatalogLeaf.cs

    /// <summary>
    /// A "package delete" catalog leaf. Represents a single package deletion event.
    /// Leafs can be discovered from a <see cref="CatalogPage"/>.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-leaf
    /// </summary>
    public class PackageDeleteCatalogLeaf : CatalogLeaf
    {
    }
}
