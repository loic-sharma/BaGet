using BaGet.Protocol;

namespace BaGet.Core.Search
{
    /// <summary>
    /// BaGet's extensions to a search request. These additions
    /// are not part of the official protocol.
    /// </summary>
    public class BaGetSearchRequest : SearchRequest
    {
        public static BaGetSearchRequest FromSearchRequest(SearchRequest request)
        {
            return new BaGetSearchRequest
            {
                Skip = request.Skip,
                Take = request.Take,
                IncludePrerelease = request.IncludePrerelease,
                IncludeSemVer2 = request.IncludeSemVer2,
                Query = request.Query,

                PackageType = null,
                Framework = null,
            };
        }

        /// <summary>
        /// The type of packages that should be returned.
        /// </summary>
        public string PackageType { get; set; }

        /// <summary>
        /// The Target Framework that results should be compatible.
        /// </summary>
        public string Framework { get; set; }
    }
}
