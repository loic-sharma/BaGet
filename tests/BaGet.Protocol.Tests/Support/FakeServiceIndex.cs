using System.Collections.Generic;
using System.Linq;

namespace BaGet.Protocol.Tests;

/// <summary>
/// Builds service index responses that advertise a chosen set of endpoints, so that a test
/// can advertise several endpoints for one resource. <see cref="TestData.ServiceIndex"/> is
/// the realistic, single-endpoint index.
/// </summary>
public static class FakeServiceIndex
{
    /// <summary>
    /// A service index that advertises the given search and autocomplete endpoints, in order,
    /// plus the package content and metadata resources every index is required to have.
    /// </summary>
    public static string Json(
        IEnumerable<string>? searchUrls = null,
        IEnumerable<string>? autocompleteUrls = null)
    {
        var resources = new List<string>
        {
            Resource(TestData.PackageContentUrl, "PackageBaseAddress/3.0.0"),
            Resource(TestData.PackageMetadataUrl, "RegistrationsBaseUrl/3.6.0"),
        };

        resources.AddRange((searchUrls ?? new[] { TestData.SearchUrl })
            .Select(url => Resource(url, "SearchQueryService/3.4.0")));

        resources.AddRange((autocompleteUrls ?? new[] { TestData.AutocompleteUrl })
            .Select(url => Resource(url, "SearchAutocompleteService/3.0.0-beta")));

        return "{\"version\":\"3.0.0\",\"resources\":[" + string.Join(",", resources) + "]}";
    }

    private static string Resource(string url, string type)
    {
        return "{\"@id\":\"" + url + "\",\"@type\":\"" + type + "\"}";
    }
}