namespace BaGet.Protocol;

/// <summary>
/// A single package type from a <see cref="SearchResult"/>.
/// 
/// See https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#search-result
/// </summary>
public class SearchResultPackageType
{
    /// <summary>
    /// The name of the package type.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }
}