using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Protocol.Models;

namespace BaGet.Protocol;

/// <summary>
/// These are documented interpretations of values returned by the Service Index resource.
/// </summary>
public static class ServiceIndexModelExtensions
{
    // See: https://github.com/NuGet/NuGet.Client/blob/e08358296db5bfa6f7f32d6f4ec8de288f3b0388/src/NuGet.Core/NuGet.Protocol/ServiceTypes.cs
    private static readonly string Version200 = "/2.0.0";
    private static readonly string Version300beta = "/3.0.0-beta";
    private static readonly string Version300 = "/3.0.0";
    private static readonly string Version340 = "/3.4.0";
    private static readonly string Version360 = "/3.6.0";
    private static readonly string Version470 = "/4.7.0";
    private static readonly string Version490 = "/4.9.0";
    private static readonly string Version500 = "/5.0.0";
    private static readonly string Version510 = "/5.1.0";

    private static readonly string[] Catalog = { "Catalog" + Version300 };
    private static readonly string[] SearchQueryService = { "SearchQueryService" + Version340, "SearchQueryService" + Version300beta, "SearchQueryService" };
    private static readonly string[] RegistrationsBaseUrl = { "RegistrationsBaseUrl" + Version360, "RegistrationsBaseUrl" + Version340, "RegistrationsBaseUrl" + Version300beta, "RegistrationsBaseUrl" };
    private static readonly string[] SearchAutocompleteService = { "SearchAutocompleteService", "SearchAutocompleteService" + Version300beta };
    private static readonly string[] ReportAbuse = { "ReportAbuseUriTemplate", "ReportAbuseUriTemplate" + Version300 };
    private static readonly string[] PackageDetailsUriTemplate = { "PackageDetailsUriTemplate" + Version510 };
    private static readonly string[] LegacyGallery = { "LegacyGallery" + Version200 };
    private static readonly string[] PackagePublish = { "PackagePublish" + Version200 };
    private static readonly string[] PackageBaseAddress = { "PackageBaseAddress" + Version300 };
    private static readonly string[] RepositorySignatures = { "RepositorySignatures" + Version500, "RepositorySignatures" + Version490, "RepositorySignatures" + Version470 };
    private static readonly string[] SymbolPackagePublish = { "SymbolPackagePublish" + Version490 };

    public static string? GetPackageContentResourceUrl(this ServiceIndexResponse? serviceIndex)
    {
        return serviceIndex.GetRequiredResourceUrl(PackageBaseAddress, nameof(PackageBaseAddress));
    }

    public static string? GetPackageMetadataResourceUrl(this ServiceIndexResponse? serviceIndex)
    {
        return serviceIndex.GetRequiredResourceUrl(RegistrationsBaseUrl, nameof(RegistrationsBaseUrl));
    }

    public static string? GetSearchQueryResourceUrl(this ServiceIndexResponse? serviceIndex)
    {
        return serviceIndex.GetRequiredResourceUrl(SearchQueryService, nameof(SearchQueryService));
    }

    /// <summary>
    /// All the Search resource's endpoints, in the order a client should try them.
    /// A service index may advertise several endpoints for this resource.
    /// </summary>
    public static IReadOnlyList<string?> GetSearchQueryResourceUrls(this ServiceIndexResponse? serviceIndex)
    {
        return serviceIndex.GetRequiredResourceUrls(SearchQueryService, nameof(SearchQueryService));
    }

    /// <summary>
    /// All the Autocomplete resource's endpoints, in the order a client should try them.
    /// The list is empty if the service index does not advertise this optional resource.
    /// </summary>
    public static IReadOnlyList<string?> GetSearchAutocompleteResourceUrls(this ServiceIndexResponse? serviceIndex)
    {
        return serviceIndex.GetResourceUrls(SearchAutocompleteService);
    }

    public static string? GetCatalogResourceUrl(this ServiceIndexResponse? serviceIndex)
    {
        return serviceIndex.GetResourceUrl(Catalog);
    }

    public static string? GetSearchAutocompleteResourceUrl(this ServiceIndexResponse? serviceIndex)
    {
        return serviceIndex.GetResourceUrl(SearchAutocompleteService);
    }

    public static string? GetResourceUrl(this ServiceIndexResponse? serviceIndex, string[] types)
    {
        return serviceIndex.GetResourceUrls(types).FirstOrDefault();
    }

    /// <summary>
    /// All the endpoints that the service index advertises for a resource, in the order a
    /// client should try them: the most preferred resource type first and, within a type,
    /// the order the service index itself used.
    ///
    /// A URL advertised under several of the resource's type versions is returned once, so
    /// that a failover does not retry an endpoint that has already failed.
    /// </summary>
    /// <param name="serviceIndex">The service index to read.</param>
    /// <param name="types">The resource's type identifiers, most preferred first.</param>
    /// <returns>The resource's endpoints. Empty if the resource is not advertised.</returns>
    public static IReadOnlyList<string?> GetResourceUrls(this ServiceIndexResponse? serviceIndex, string[] types)
    {
        if (serviceIndex == null) throw new ArgumentNullException(nameof(serviceIndex));
        if (types == null) throw new ArgumentNullException(nameof(types));

        if (serviceIndex.Resources == null)
        {
            return Array.Empty<string>();
        }

        return types
            .SelectMany(type => serviceIndex.Resources.Where(resource => resource.Type == type))
            .Select(resource => resource.ResourceUrl?.Trim('/'))
            .Where(resourceUrl => !string.IsNullOrEmpty(resourceUrl))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static string? GetRequiredResourceUrl(this ServiceIndexResponse? serviceIndex, string[] types, string resourceName)
    {
        return serviceIndex.GetRequiredResourceUrls(types, resourceName)[0];
    }

    /// <summary>
    /// All the endpoints that the service index advertises for a required resource.
    /// </summary>
    /// <param name="serviceIndex">The service index to read.</param>
    /// <param name="types">The resource's type identifiers, most preferred first.</param>
    /// <param name="resourceName">The resource's name, used in the error message.</param>
    /// <returns>The resource's endpoints, in the order a client should try them.</returns>
    public static IReadOnlyList<string?> GetRequiredResourceUrls(
        this ServiceIndexResponse? serviceIndex,
        string[] types,
        string resourceName)
    {
        // For more information on required resources,
        // see: https://docs.microsoft.com/en-us/nuget/api/overview#resources-and-schema
        var resourceUrls = serviceIndex.GetResourceUrls(types);
        if (resourceUrls.Count == 0)
        {
            throw new InvalidOperationException(
                $"The service index does not have a resource named '{resourceName}'");
        }

        return resourceUrls;
    }
}