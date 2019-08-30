namespace BaGet.Core.Search
{
    /// <summary>
    /// The request to find the packages that depend on a given package.
    /// This is an unofficial API that isn't part of the NuGet protocol.
    /// </summary>
    public class DependentsRequest
    {
        /// <summary>
        /// How many results to skip.
        /// </summary>
        public int Skip { get; set; }

        /// <summary>
        /// How many results to return.
        /// </summary>
        public int Take { get; set; }

        /// <summary>
        /// The package whose dependents should be found.
        /// </summary>
        public string PackageId { get; set; }
    }
}
