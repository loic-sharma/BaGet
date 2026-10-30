using System;
using System.Globalization;
using BaGet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;

namespace BaGet.Web.V3.Routing;

// The URLs below are built from the incoming request's "Host" header, which the client
// controls. The header is validated against the configured allow-list by ASP.NET Core's
// host filtering middleware instead of here: see BaGetOptions.AllowedHosts, which BaGet's
// Startup binds to HostFilteringOptions and enforces with UseHostFiltering.
//
// Validating in this generator would be both too late and too narrow. Too late, because
// by the time a URL is generated the request has already been handled on a host BaGet
// does not serve, and there is no correct URL left to return. Too narrow, because the
// same header is read by routing, by LinkGenerator below and by anything else holding
// the HttpContext; only rejecting the request protects all of them at once.
/// <summary>
/// Builds the absolute URLs of the NuGet protocol inside the current repository (<see cref="IRepositoryContext"/>):
/// the default repository's at the root, any other's under <c>/r/{repo}/</c>, both after the path base. A URL never
/// leaves the repository that advertised it.
/// </summary>
public class BaGetUrlGenerator : IUrlGenerator
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly LinkGenerator _linkGenerator;

    public BaGetUrlGenerator(IHttpContextAccessor httpContextAccessor, LinkGenerator linkGenerator)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _linkGenerator = linkGenerator ?? throw new ArgumentNullException(nameof(linkGenerator));
    }

    public string? GetServiceIndexUrl()
    {
        #pragma warning disable CS8604
        return Link(Routes.IndexRouteName, null);
        #pragma warning restore CS8604
    }

    public string GetPackageContentResourceUrl()
    {
        return AbsoluteUrl("v3/package");
    }

    public string GetPackageMetadataResourceUrl()
    {
        return AbsoluteUrl("v3/registration");
    }

    public string? GetPackagePublishResourceUrl()
    {
        #pragma warning disable CS8604
        return Link(Routes.UploadPackageRouteName, null);
        #pragma warning restore CS8604
    }

    public string? GetSymbolPublishResourceUrl()
    {
        #pragma warning disable CS8604
        return Link(Routes.UploadSymbolRouteName, null);
        #pragma warning restore CS8604
    }

    public string? GetSearchResourceUrl()
    {
        #pragma warning disable CS8604
        return Link(Routes.SearchRouteName, null);
        #pragma warning restore CS8604
    }

    public string? GetAutocompleteResourceUrl()
    {
        #pragma warning disable CS8604
        return Link(Routes.AutocompleteRouteName, null);
        #pragma warning restore CS8604
    }

    public string? GetRegistrationIndexUrl(string? id)
    {
        #pragma warning disable CS8602, CS8604
        return Link(Routes.RegistrationIndexRouteName, new { Id = id.ToLowerInvariant() });
        #pragma warning restore CS8602, CS8604
    }

    public string GetRegistrationPageUrl(string id, NuGetVersion lower, NuGetVersion upper)
    {
        // BaGet does not support paging the registration resource.
        throw new NotImplementedException();
    }

    public string? GetRegistrationLeafUrl(string? id, NuGetVersion version)
    {
        #pragma warning disable CS8602, CS8604
        return Link(Routes.RegistrationLeafRouteName, new
            {
                Id = id.ToLowerInvariant(),
                Version = version.ToNormalizedString().ToLowerInvariant(),
            });
        #pragma warning restore CS8602, CS8604
    }

    public string? GetPackageVersionsUrl(string id)
    {
        #pragma warning disable CS8604
        return Link(Routes.PackageVersionsRouteName, new { Id = id.ToLowerInvariant() });
        #pragma warning restore CS8604
    }

    public string? GetPackageDownloadUrl(string? id, NuGetVersion version)
    {
        #pragma warning disable CS8602
        id = id.ToLowerInvariant();
        #pragma warning restore CS8602
        var versionString = version.ToNormalizedString().ToLowerInvariant();

        #pragma warning disable CS8604
        return Link(Routes.PackageDownloadRouteName, new
            {
                Id = id,
                Version = versionString,
                IdVersion = $"{id}.{versionString}"
            });
        #pragma warning restore CS8604
    }

    public string? GetPackageManifestDownloadUrl(string id, NuGetVersion version)
    {
        id = id.ToLowerInvariant();
        var versionString = version.ToNormalizedString().ToLowerInvariant();

        #pragma warning disable CS8604
        return Link(Routes.PackageDownloadRouteName, new
            {
                Id = id,
                Version = versionString,
                Id2 = id,
            });
        #pragma warning restore CS8604
    }

    public string? GetPackageIconDownloadUrl(string? id, NuGetVersion version)
    {
        #pragma warning disable CS8602
        id = id.ToLowerInvariant();
        #pragma warning restore CS8602
        var versionString = version.ToNormalizedString().ToLowerInvariant();

        #pragma warning disable CS8604
        return Link(Routes.PackageDownloadIconRouteName, new
            {
                Id = id,
                Version = versionString
            });
        #pragma warning restore CS8604
    }

    public string? GetV2FeedUrl()
    {
        #pragma warning disable CS8604
        return Link(Routes.V2RootRouteName, null);
        #pragma warning restore CS8604
    }

    // These two are templates for the client to expand, not URLs for a specific
    // package, so the placeholders are returned verbatim. Routing them through
    // LinkGenerator would percent-encode the braces.
    // The package and report-abuse pages exist under every repository (RepositoryActionRouteConvention), so each
    // repository advertises its own: AbsoluteUrl adds the "r/{name}/" prefix.
    public string? GetPackageDetailsUriTemplate()
    {
        return AbsoluteUrl("packages/{id}/{version}");
    }

    public string GetNpmRegistryUrl()
    {
        return AbsoluteUrl("npm/");
    }

    public string GetMavenRepositoryUrl()
    {
        return AbsoluteUrl("maven/");
    }

    public string GetOciRegistryUrl()
    {
        #pragma warning disable CS8602
        var request = _httpContextAccessor.HttpContext.Request;
        #pragma warning restore CS8602

        return string.Concat(request.Scheme, "://", request.Host.ToUriComponent(), request.PathBase.ToUriComponent(), "/v2/");
    }

    public (string Host, bool IsHttp) GetOciRegistryHost()
    {
        #pragma warning disable CS8602
        var request = _httpContextAccessor.HttpContext.Request;
        #pragma warning restore CS8602

        return (request.Host.ToUriComponent(), string.Equals(request.Scheme, "http", StringComparison.OrdinalIgnoreCase));
    }

    public string GetVulnerabilityIndexUrl()
    {
        return AbsoluteUrl("v3/vulnerabilities/index.json");
    }

    public string GetVulnerabilityPageUrl(string pageName)
    {
        return AbsoluteUrl($"v3/vulnerabilities/{Uri.EscapeDataString(pageName)}.json");
    }

    public string GetCatalogIndexUrl()
    {
        return AbsoluteUrl("v3/catalog/index.json");
    }

    public string GetCatalogPageUrl(int pageIndex)
    {
        return AbsoluteUrl($"v3/catalog/page{pageIndex.ToString(CultureInfo.InvariantCulture)}.json");
    }

    public string GetCatalogLeafUrl(string? commitId)
    {
        #pragma warning disable CS8604
        return AbsoluteUrl($"v3/catalog/data/{Uri.EscapeDataString(commitId)}.json");
        #pragma warning restore CS8604
    }

    public string GetRepositorySignaturesUrl()
    {
        return AbsoluteUrl("v3/repository-signatures/index.json");
    }

    public string GetRepositorySigningCertificateUrl(string fingerprint)
    {
        return AbsoluteUrl($"v3/repository-signatures/{Uri.EscapeDataString(fingerprint)}.crt");
    }

    public string? GetReportAbuseUriTemplate()
    {
        return AbsoluteUrl("packages/{id}/{version}/report-abuse");
    }

    private string AbsoluteUrl(string relativePath)
    {
        #pragma warning disable CS8602
        var request = _httpContextAccessor.HttpContext.Request;
        #pragma warning restore CS8602

        return string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent(),
            "/",
            RepositoryPrefix(),
            relativePath);
    }

    /// <summary>
    /// The repository of the request being served. Read from the request's own services on every call, never
    /// injected: singletons (the registration and search builders) hold this generator for the life of the process,
    /// and a scoped <see cref="IRepositoryContext"/> captured at construction would pin them to one repository.
    /// </summary>
    private RepositoryInfo CurrentRepository()
        => _httpContextAccessor.HttpContext?.RequestServices.GetService<IRepositoryContext>()?.Current
           ?? RepositoryInfo.Default;

    /// <summary>
    /// "r/{name}/" for a non-default repository, else nothing.
    /// </summary>
    private string RepositoryPrefix()
    {
        return CurrentRepository().PathPrefix;
    }

    /// <summary>
    /// The absolute URL of a named protocol route, in the current repository: the route's <c>r/{repo}/</c> variant
    /// for a non-default one.
    /// </summary>
    private string? Link(string routeName, object? values)
    {
        var current = CurrentRepository();

        if (!current.IsDefault)
        {
            var routeValues = new RouteValueDictionary(values)
            {
                [Routes.RepositoryRouteKey] = current.Name,
            };

            routeName = Routes.RepositoryRouteName(routeName);
            values = routeValues;
        }

        #pragma warning disable CS8604
        return _linkGenerator.GetUriByRouteValues(_httpContextAccessor.HttpContext, routeName, values);
        #pragma warning restore CS8604
    }
}