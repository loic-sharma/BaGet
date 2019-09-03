namespace BaGet.Protocol
{
    /// <summary>
    /// The type of a <see cref="CatalogLeaf"/>.
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
