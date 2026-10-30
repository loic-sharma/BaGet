using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol.Tests;

// Based off https://github.com/NuGet/NuGet.Services.Metadata/blob/0c69b9dd47e01125c22f72c1e27cf3cdafc65233/tests/NuGet.Protocol.Catalog.Tests/TestDataHttpMessageHandler.cs
public class TestDataHttpMessageHandler : HttpMessageHandler
{
    private static readonly Dictionary<string, Func<string>> UrlToGetContent = new()
    {
        { TestData.ServiceIndexUrl, () => TestData.ServiceIndex },

        { TestData.CatalogIndexUrl, () => TestData.CatalogIndex },
        { TestData.CatalogPageUrl, () => TestData.CatalogPage },
        { TestData.PackageDeleteCatalogLeafUrl, () => TestData.PackageDeleteCatalogLeaf },
        { TestData.PackageDetailsCatalogLeafUrl, () => TestData.PackageDetailsCatalogLeaf },
        //{ TestData.CatalogLeafInvalidDependencyVersionRangeUrl, () => TestData.CatalogLeafInvalidDependencyVersionRange },

        { TestData.RegistrationIndexInlinedItemsUrl, () => TestData.RegistrationIndexInlinedItems },
        { TestData.RegistrationIndexPagedItemsUrl, () => TestData.RegistrationIndexPagedItems },
        { TestData.RegistrationLeafUnlistedUrl, () => TestData.RegistrationLeafUnlisted },
        { TestData.RegistrationLeafListedUrl, () => TestData.RegistrationLeafListed },
        { TestData.RegistrationPageUrl, () => TestData.RegistrationPage },
        { TestData.RegistrationPageFirstUrl, () => TestData.RegistrationPageFirst },
        { TestData.RegistrationPageDeprecatedUrl, () => TestData.RegistrationPageDeprecated },

        { TestData.PackageContentVersionListUrl, () => TestData.PackageContentVersionList },
        { TestData.PagedPackageContentVersionListUrl, () => TestData.PagedPackageContentVersionList },
        { TestData.PackageContentNupkgUrl, () => TestData.PackageContentNupkg },
        { TestData.PackageContentNuspecUrl, () => TestData.PackageContentNuspec },

        { TestData.DefaultSearchUrl, () => TestData.DefaultSearch },
        { TestData.DefaultAutocompleteUrl, () => TestData.DefaultAutocomplete },
        { TestData.AutocompleteVersionsUrl, () => TestData.AutocompleteVersions },
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Send(request));
    }

    private HttpResponseMessage Send(HttpRequestMessage request)
    {
        Func<string>? getContent;
        #pragma warning disable CS8602
        if (request.Method != HttpMethod.Get
            || !UrlToGetContent.TryGetValue(request.RequestUri.AbsoluteUri, out getContent))
        #pragma warning restore CS8602
        {
            return new HttpResponseMessage
            {
                RequestMessage = request,
                StatusCode = HttpStatusCode.NotFound,
                Content = new StringContent(string.Empty),
            };
        }

        return new HttpResponseMessage
        {
            RequestMessage = request,
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(
                getContent(),
                encoding: null,
                mediaType: MediaTypeFor(request.RequestUri)),
        };
    }

    private static string MediaTypeFor(Uri requestUri)
    {
        var path = requestUri.AbsolutePath;

        if (path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            return "application/octet-stream";
        }

        if (path.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
        {
            return "application/xml";
        }

        return "application/json";
    }
}