namespace BaGet.Protocol.Models
{
    // This class is based off https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Models/CatalogLeafType.cs

    /// <summary>
    /// The type of a <see cref="CatalogLeaf"/>.
    ///
    /// See https://docs.microsoft.com/en-us/nuget/api/catalog-resource#item-types
    /// </summary>
    public enum CatalogLeafType
    {
        /// <summary>
        /// The <see cref="CatalogLeaf"/> represents the snapshot of a package's metadata.
        /// </summary>
        PackageDetails = 1,

        /// <summary>
        /// The <see cref="CatalogLeaf"/> represents a package that was deleted.
        /// </summary>
        PackageDelete = 2,
    }
}
