namespace BaGet.Core
{
    /// <summary>
    /// How BaGet should interpret package deletion requests.
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#delete-a-package
    /// </summary>
    public enum PackageDeletionBehavior
    {
        /// <summary>
        /// Package "deletions" make the package undiscoverable. The package is still restorable
        /// by consumers that know its id and version. This is the recommended behavior as it prevents
        /// the "left pad" problem.
        /// </summary>
        Unlist,

        /// <summary>
        /// Removes the package from the database and storage. Existing consumers will no longer
        /// be able to restore the package.
        /// </summary>
        HardDelete,
    }
}
