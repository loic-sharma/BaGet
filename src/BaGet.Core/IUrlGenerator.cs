using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// Used to create URLs to resources in the NuGet protocol.
/// </summary>
public interface IUrlGenerator
{
    /// <summary>
    /// Get the URL for the package source (also known as the "service index").
    /// See: https://docs.microsoft.com/en-us/nuget/api/service-index
    /// </summary>
    string? GetServiceIndexUrl();

    /// <summary>
    /// Get the URL for the root of the package content resource.
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
    /// </summary>
    string GetPackageContentResourceUrl();

    /// <summary>
    /// Get the URL for the root of the package metadata resource.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
    /// </summary>
    string GetPackageMetadataResourceUrl();

    /// <summary>
    /// Get the URL to publish packages.
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource
    /// </summary>
    string? GetPackagePublishResourceUrl();

    /// <summary>
    /// Get the URL to publish symbol packages.
    /// See: https://docs.microsoft.com/en-us/nuget/api/symbol-package-publish-resource
    /// </summary>
    string? GetSymbolPublishResourceUrl();

    /// <summary>
    /// Get the URL to search for packages.
    /// See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
    /// </summary>
    string? GetSearchResourceUrl();

    /// <summary>
    /// Get the URL to autocomplete package IDs.
    /// See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
    /// </summary>
    string? GetAutocompleteResourceUrl();

    /// <summary>
    /// Get the URL of the V2 (OData) feed, for clients that cannot use V3.
    /// </summary>
    string? GetV2FeedUrl();

    /// <summary>
    /// Get the URI template a client expands to reach a package's details page.
    /// The literal "{id}" and "{version}" placeholders are part of the value.
    /// See: https://learn.microsoft.com/en-us/nuget/api/service-index#resources
    /// </summary>
    /// <returns>
    /// The template, in the current repository (<c>/r/{name}/packages/…</c> for a non-default one, as the pages exist
    /// under every repository). Null is reserved for a generator whose host has no package pages, in which case the
    /// service index leaves the resource out.
    /// </returns>
    string? GetPackageDetailsUriTemplate();

    /// <summary>
    /// Get the URI template a client expands to report a package as abusive.
    /// The literal "{id}" and "{version}" placeholders are part of the value.
    /// See: https://learn.microsoft.com/en-us/nuget/api/service-index#resources
    /// </summary>
    /// <returns>The template, or null when the host has no such page; see <see cref="GetPackageDetailsUriTemplate"/>.</returns>
    string? GetReportAbuseUriTemplate();

    /// <summary>
    /// Get the URL of the vulnerability index, which lists the vulnerability pages.
    /// See: https://learn.microsoft.com/en-us/nuget/api/vulnerability-info
    /// </summary>
    string GetVulnerabilityIndexUrl();

    /// <summary>
    /// Get the URL of one page of vulnerability data.
    /// </summary>
    /// <param name="pageName">The page's name, such as "base".</param>
    string GetVulnerabilityPageUrl(string pageName);

    /// <summary>
    /// Get the URL of the catalog index.
    /// See: https://learn.microsoft.com/en-us/nuget/api/catalog-resource
    /// </summary>
    string GetCatalogIndexUrl();

    /// <summary>
    /// Get the URL of one catalog page.
    /// </summary>
    /// <param name="pageIndex">The page's zero based index.</param>
    string GetCatalogPageUrl(int pageIndex);

    /// <summary>
    /// Get the URL of one catalog leaf.
    /// </summary>
    /// <param name="commitId">The ID of the commit that wrote the leaf.</param>
    string GetCatalogLeafUrl(string? commitId);

    /// <summary>
    /// Get the URL of the repository signatures resource.
    /// See: https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource
    /// </summary>
    string GetRepositorySignaturesUrl();

    /// <summary>
    /// Get the URL to download a repository signing certificate.
    /// </summary>
    /// <param name="fingerprint">The certificate's SHA-256 fingerprint, as lowercase hex.</param>
    string GetRepositorySigningCertificateUrl(string fingerprint);

    /// <summary>
    /// Get the URL for the entry point of a package's metadata.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-index
    /// </summary>
    /// <param name="id">The package's ID.</param>
    string? GetRegistrationIndexUrl(string? id);

    /// <summary>
    /// Get the URL for the metadata of several versions of a single package.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page
    /// </summary>
    /// <param name="id">The package's ID</param>
    /// <param name="lower">The lowest SemVer 2.0.0 version in the page (inclusive)</param>
    /// <param name="upper">The highest SemVer 2.0.0 version in the page (inclusive)</param>
    string GetRegistrationPageUrl(string id, NuGetVersion lower, NuGetVersion upper);

    /// <summary>
    /// Get the URL for the metadata of a specific package ID and version.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-leaf
    /// </summary>
    /// <param name="id">The package's ID</param>
    /// <param name="version">The package's version</param>
    string? GetRegistrationLeafUrl(string? id, NuGetVersion version);

    /// <summary>
    /// Get the URL that lists a package's versions.
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#enumerate-package-versions
    /// </summary>
    /// <param name="id">The package's ID</param>
    string? GetPackageVersionsUrl(string id);

    /// <summary>
    /// Get the URL to download a package (.nupkg).
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#download-package-content-nupkg
    /// </summary>
    /// <param name="id">The package's ID</param>
    /// <param name="version">The package's version</param>
    string? GetPackageDownloadUrl(string? id, NuGetVersion version);

    /// <summary>
    /// Get the URL to download a package's manifest (.nuspec).
    /// </summary>
    /// <param name="id">The package's ID</param>
    /// <param name="version">The package's version</param>
    string? GetPackageManifestDownloadUrl(string id, NuGetVersion version);

    /// <summary>
    /// Get the URL to download a package icon.
    /// </summary>
    /// <param name="id">The package's ID</param>
    /// <param name="version">The package's version</param>
    string? GetPackageIconDownloadUrl(string? id, NuGetVersion version);

    /// <summary>
    /// Get the address an npm client is configured with for the current repository: <c>npm/</c> below the repository base,
    /// with a trailing slash (<c>https://host/r/web/npm/</c>). Tarball URLs are this plus <c>{name}/-/{file}</c>.
    /// </summary>
    string GetNpmRegistryUrl();

    /// <summary>
    /// Get the address a Maven client is configured with for the current repository: <c>maven/</c> below the repository base,
    /// with a trailing slash (<c>https://host/r/libs/maven/</c>). A file is this plus its layout path.
    /// </summary>
    string GetMavenRepositoryUrl();

    /// <summary>
    /// Get the root of the OCI registry: <c>v2/</c> at the server root (after the path base), with a trailing slash
    /// (<c>https://host/v2/</c>). A registry is not under a repository's <c>r/{repo}/</c> prefix: the BaGet repository is the
    /// first segment after <c>v2/</c>, so the address of an image is this plus <c>{repo}/{image}/…</c>.
    /// </summary>
    string GetOciRegistryUrl();

    /// <summary>
    /// Get the registry host as a Docker client writes it in an image reference (<c>host:port</c>, no scheme), and whether the
    /// request came over plain http (a daemon must then list it as an insecure registry).
    /// </summary>
    (string Host, bool IsHttp) GetOciRegistryHost();
}