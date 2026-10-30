using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Core;

public class BaGetServiceIndex : IServiceIndexService
{
    private readonly IUrlGenerator _url;
    private readonly IReadOnlyList<IServiceIndexResourceProvider> _providers;

    public BaGetServiceIndex(IUrlGenerator url)
        : this(url, Array.Empty<IServiceIndexResourceProvider>())
    {
    }

    public BaGetServiceIndex(IUrlGenerator url, IEnumerable<IServiceIndexResourceProvider>? providers)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
    }

    private IEnumerable<ServiceIndexItem> BuildResource(string name, string? url, params string[] versions)
    {
        foreach (var version in versions)
        {
            var type = string.IsNullOrEmpty(version) ? name : $"{name}/{version}";

            yield return new ServiceIndexItem
            {
                ResourceUrl = url,
                Type = type,
            };
        }
    }

    public Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var resources = new List<ServiceIndexItem>();

        resources.AddRange(BuildResource("PackagePublish", _url.GetPackagePublishResourceUrl(), "2.0.0"));
        resources.AddRange(BuildResource("SymbolPackagePublish", _url.GetSymbolPublishResourceUrl(), "4.9.0"));
        resources.AddRange(BuildResource("SearchQueryService", _url.GetSearchResourceUrl(), "", "3.0.0-beta", "3.0.0-rc"));
        resources.AddRange(BuildResource("RegistrationsBaseUrl", _url.GetPackageMetadataResourceUrl(), "", "3.0.0-rc", "3.0.0-beta"));
        resources.AddRange(BuildResource("PackageBaseAddress", _url.GetPackageContentResourceUrl(), "3.0.0"));
        resources.AddRange(BuildResource("SearchAutocompleteService", _url.GetAutocompleteResourceUrl(), "", "3.0.0-rc", "3.0.0-beta"));

        // The web pages these templates expand to exist under every repository (/r/{name}/packages/…), so each
        // repository's index advertises its own; a host without pages returns null and leaves them out.
        var detailsTemplate = _url.GetPackageDetailsUriTemplate();
        if (detailsTemplate != null)
        {
            resources.AddRange(BuildResource("PackageDetailsUriTemplate", detailsTemplate, "5.1.0"));
        }

        var abuseTemplate = _url.GetReportAbuseUriTemplate();
        if (abuseTemplate != null)
        {
            resources.AddRange(BuildResource("ReportAbuseUriTemplate", abuseTemplate, "3.0.0-beta", "3.0.0-rc"));
        }

        resources.AddRange(BuildResource("VulnerabilityInfo", _url.GetVulnerabilityIndexUrl(), "6.7.0"));

        foreach (var provider in _providers)
        {
            resources.AddRange(provider.GetResources());
        }

        var result = new ServiceIndexResponse
        {
            Version = "3.0.0",
            Resources = resources,
        };

        return Task.FromResult(result);
    }
}