using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BaGet.Frontend.Mvc.UI;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests.Contracts;

/// <summary>
/// The contract of the JSON UI API: every <c>/api/ui</c> endpoint (route template including the <c>api/ui/r/{repo}/</c> twins, the HTTP methods, the
/// screen it serves and the page handler it mirrors) is listed in <c>Contracts/ui-api.txt</c>. Unlike <c>ui-routes.txt</c> this file grows as
/// the tracks split screens; an existing line changes only for a reviewed, deliberate change of the API (regenerate with
/// <c>BAGET_UPDATE_CONTRACTS=1</c>).
/// </summary>
public class UiApiContractTests : IDisposable
{
    private readonly BaGetApplication _app;

    public UiApiContractTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "Validation:Enabled", "true" },
            { "Feeds:EnableV2", "true" },
        });
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void TheUiApiRoutesMatchTheSnapshot()
    {
        var lines = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var template = (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            if (!template.StartsWith("api/ui/", StringComparison.OrdinalIgnoreCase)) continue;

            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            var api = action?.MethodInfo.GetCustomAttribute<UiApiAttribute>();
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Select(m => m.ToUpperInvariant()).OrderBy(m => m, StringComparer.Ordinal) ?? Enumerable.Empty<string>();

            lines.Add($"{template.ToLowerInvariant()}\t{string.Join(" ", methods)}\t{api?.Screen.ToString() ?? "-"}\t{api?.Handler ?? "-"}\torder={endpoint.Order}");
        }

        Assert.True(lines.Count >= 5, "expected the antiforgery endpoint and the pilot screens (legal, statistics) with their repository twins");

        ContractSnapshot.Verify("ui-api.txt", string.Join("\n", lines));
    }
}
