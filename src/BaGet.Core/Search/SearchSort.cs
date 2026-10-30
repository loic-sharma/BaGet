namespace BaGet.Core;

/// <summary>
/// How the web UI orders search results. The V3 search API has no sort parameter, so
/// it always uses <see cref="Relevance"/>.
/// </summary>
public enum SearchSort
{
    /// <summary>
    /// The provider's default order.
    /// </summary>
    Relevance,

    /// <summary>
    /// Most downloaded package first.
    /// </summary>
    TotalDownloads,

    /// <summary>
    /// Most recently published package first.
    /// </summary>
    LastUpdated,
}
