using System.Collections.Generic;
using System.Text.Json.Serialization;
using BaGet.Protocol.Internal;

namespace BaGet.Protocol.Models;

/// <summary>
/// A package that matched a search query.
/// 
/// See https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#search-result
/// </summary>
public class SearchResult
{
    /// <summary>
    /// The ID of the matched package.
    /// </summary>
    [JsonPropertyName("id")]
    public string? PackageId { get; set; }

    /// <summary>
    /// The latest version of the matched pacakge. This is the full NuGet version after normalization,
    /// including any SemVer 2.0.0 build metadata.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    /// The description of the matched package.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// The authors of the matched package.
    /// </summary>
    [JsonPropertyName("authors")]
    [JsonConverter(typeof(StringOrStringArrayJsonConverter))]
    public IReadOnlyList<string>? Authors { get; set; }

    /// <summary>
    /// The URL of the matched package's icon.
    /// </summary>
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    /// <summary>
    /// The URL of the matched package's license.
    /// </summary>
    [JsonPropertyName("licenseUrl")]
    public string? LicenseUrl { get; set; }

    /// <summary>
    /// The package types defined by the package author.
    /// </summary>
    [JsonPropertyName("packageTypes")]
    public IReadOnlyList<SearchResultPackageType>? PackageTypes { get; set; }

    /// <summary>
    /// The URL of the matched package's homepage.
    /// </summary>
    [JsonPropertyName("projectUrl")]
    public string? ProjectUrl { get; set; }

    /// <summary>
    /// The URL for the matched package's registration index.
    /// </summary>
    [JsonPropertyName("registration")]
    public string? RegistrationIndexUrl { get; set; }

    /// <summary>
    /// The summary of the matched package.
    /// </summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>
    /// The tags of the matched package.
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; set; }

    /// <summary>
    /// The title of the matched package.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// The total downloads for all versions of the matched package.
    /// </summary>
    [JsonPropertyName("totalDownloads")]
    public long TotalDownloads { get; set; }

    /// <summary>
    /// The versions of the matched package.
    /// </summary>
    [JsonPropertyName("versions")]
    public IReadOnlyList<SearchResultVersion>? Versions { get; set; }

    /// <summary>
    /// The latest version's deprecation, or null if it is not deprecated.
    /// </summary>
    [JsonPropertyName("deprecation")]
    public PackageDeprecation? Deprecation { get; set; }

    /// <summary>
    /// The latest version's known vulnerabilities, or null if it has none.
    /// </summary>
    [JsonPropertyName("vulnerabilities")]
    public IReadOnlyList<PackageVulnerabilityMetadata>? Vulnerabilities { get; set; }

    /// <summary>
    /// The package's owners, or null if it has none.
    /// </summary>
    [JsonPropertyName("owners")]
    public IReadOnlyList<string?>? Owners { get; set; }

    /// <summary>
    /// Whether the package ID is reserved for its owners. Null if it has no owners.
    /// </summary>
    [JsonPropertyName("verified")]
    public bool? Verified { get; set; }
}