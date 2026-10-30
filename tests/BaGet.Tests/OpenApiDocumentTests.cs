using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Mvc.UI;
using BaGet.Web.V3.OpenApi;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.Routing;

namespace BaGet.Tests;

/// <summary>
/// The OpenAPI documents (<c>ui</c>, <c>v1</c>, <c>protocols</c>) and the Scalar viewer: what they list (every <c>/api/v1</c> action,
/// every protocol route in its plain and <c>r/{repo}/</c> variants, whatever <c>/api/ui</c> holds) and who may read them
/// (administrators only: cookie or <c>admin:api</c> key; anyone else 404).
/// </summary>
public class OpenApiDocumentTests : IDisposable
{
    private readonly AdminApiHarness _api;
    private readonly ITestOutputHelper _output;

    public OpenApiDocumentTests(ITestOutputHelper output)
    {
        _output = output;
        _api = new AdminApiHarness(output);
    }

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task TheUiDocumentDescribesEnumsAsTheNamesTheApiWrites()
    {
        var schemas = (await _api.GetAsync("/openapi/ui.json")).Json["components"]!["schemas"]!.AsObject();

        Assert.Equal("string", schemas["ScreenStatus"]!["type"]!.GetValue<string>());
        Assert.Equal(
            new[] { "Ok", "Invalid", "NotFound", "Forbidden", "SignIn", "Next" },
            schemas["ScreenStatus"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
        Assert.Contains("TotalDownloads", schemas["SearchSort"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));

        // No enum is described as a number, and no integer as "integer or a string matching a pattern".
        foreach (var (name, schema) in schemas)
        {
            if (schema!["enum"] != null) Assert.True(schema["type"]?.GetValue<string>() == "string", $"{name} is an enum that is not a string");
        }

        var status = schemas["ScreenResultOfPackageScreen"]!["properties"]!["statusCode"]!;
        Assert.Equal("integer", status["type"]!.GetValue<string>());
        Assert.Null(status["anyOf"]);
        Assert.Null(status["pattern"]);
    }

    [Fact]
    public async Task TheUiDocumentTypesTheErrorAnswersLikeTheOkAnswer()
    {
        var paths = (await _api.GetAsync("/openapi/ui.json")).Json["paths"]!.AsObject();

        var checkedOperations = 0;
        foreach (var (path, item) in paths)
        {
            foreach (var (method, operation) in item!.AsObject())
            {
                var responses = operation!["responses"]!.AsObject();
                var schema = responses["200"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
                if (schema == null || !schema.Contains("ScreenResultOf", StringComparison.Ordinal)) continue;

                checkedOperations++;
                foreach (var code in new[] { "400", "401", "403", "404" })
                {
                    Assert.True(
                        responses[code]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>() == schema,
                        $"{method} {path} does not describe {code} with {schema}");
                }
            }
        }

        Assert.True(checkedOperations > 100, "the screens' operations were not found");
    }

    [Fact]
    public async Task TheUiDocumentPublishesAnOptionalRouteValueAsAPathOfItsOwn()
    {
        var paths = (await _api.GetAsync("/openapi/ui.json")).Json["paths"]!.AsObject();

        static (string Name, string In, bool Required)[] Parameters(JsonNode? path)
            => path!["get"]!["parameters"]!.AsArray().Select(p => (p!["name"]!.GetValue<string>(), p["in"]!.GetValue<string>(), p["required"]?.GetValue<bool>() == true)).ToArray();

        foreach (var prefix in new[] { "/api/ui", "/api/ui/r/{repo}" })
        {
            var without = Parameters(paths[prefix + "/packages/{id}"]);
            var with = Parameters(paths[prefix + "/packages/{id}/{version}"]);
            Assert.DoesNotContain(without, p => p.Name == "version");
            Assert.Contains(("version", "path", true), with);
            Assert.Contains(("id", "path", true), without);

            Assert.NotNull(paths[prefix + "/maven-artifacts/{group}/{artifact}"]);
            Assert.Contains(("version", "path", true), Parameters(paths[prefix + "/maven-artifacts/{group}/{artifact}/{version}"]));
        }

        // Whatever a path template names is a path parameter of every operation under it (a client builds the URL from them).
        foreach (var (path, item) in paths)
        {
            var names = System.Text.RegularExpressions.Regex.Matches(path, @"\{([^}]+)\}").Select(m => m.Groups[1].Value).ToList();
            foreach (var (method, operation) in item!.AsObject())
            {
                var declared = operation!["parameters"]?.AsArray().Where(p => p!["in"]!.GetValue<string>() == "path").Select(p => p!["name"]!.GetValue<string>()).ToList() ?? new List<string>();
                foreach (var name in names) Assert.True(declared.Contains(name), $"{method} {path} does not declare the path parameter {name}");
            }
        }
    }

    [Fact]
    public async Task TheV1DocumentListsEveryAdministrationOperationAndTheAliasServesTheSameDocument()
    {
        var document = await _api.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, document.Status);
        Assert.StartsWith("3.0", document.Json["openapi"]!.GetValue<string>());
        Assert.Equal("BaGet administration API", document.Json["info"]!["title"]!.GetValue<string>());

        var paths = document.Json["paths"]!.AsObject();
        var endpoints = ApiCatalog.Endpoints(_api.App.Services.GetRequiredService<IActionDescriptorCollectionProvider>());
        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            Assert.True(paths[endpoint.Path]?[endpoint.Method.ToLowerInvariant()] != null, $"{endpoint.Method} {endpoint.Path} is not in the v1 document.");
        }

        Assert.All(paths.Select(p => p.Key), path => Assert.StartsWith("/api/v1/", path));

        var alias = await _api.GetAsync("/api/v1/openapi.json");
        Assert.Equal(HttpStatusCode.OK, alias.Status);
        Assert.Equal(document.Json.ToJsonString(), alias.Json.ToJsonString());
    }

    [Fact]
    public async Task TheProtocolsDocumentListsEveryProtocolRouteInItsPlainAndRepositoryVariants()
    {
        var document = await _api.GetAsync("/openapi/protocols.json");
        Assert.Equal(HttpStatusCode.OK, document.Status);
        var paths = document.Json["paths"]!.AsObject();

        var routes = _api.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<RepositoryRouteMetadata>() != null && !string.IsNullOrEmpty(e.RoutePattern.RawText))
            .ToList();
        Assert.True(routes.Count > 50, "the protocol routes were not found");

        foreach (var route in routes)
        {
            var path = "/" + ProtocolsDocumentTransformer.CleanPath(route.RoutePattern.RawText!);
            var methods = route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" };
            foreach (var method in methods)
            {
                Assert.True(paths[path]?[method.ToLowerInvariant()] != null, $"{method} {path} is not in the protocols document.");
            }
        }

        // The plain route and its r/{repo}/ twin, for each protocol.
        foreach (var path in new[] { "/v3/index.json", "/r/{repo}/v3/index.json", "/v3/search", "/r/{repo}/v3/search" })
        {
            Assert.NotNull(paths[path]?["get"]);
        }

        Assert.Contains(paths.Select(p => p.Key), p => p.Contains("npm", StringComparison.Ordinal));
        Assert.Contains(paths.Select(p => p.Key), p => p.Contains("maven", StringComparison.Ordinal));
        Assert.Contains(paths.Select(p => p.Key), p => p.StartsWith("/v2/", StringComparison.Ordinal));

        var parameter = paths["/r/{repo}/v3/index.json"]!["get"]!["parameters"]!.AsArray().Single()!;
        Assert.Equal("repo", parameter["name"]!.GetValue<string>());
        Assert.Equal("path", parameter["in"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheUiDocumentListsEveryUiApiActionInItsPlainAndRepositoryVariants()
    {
        var document = await _api.GetAsync("/openapi/ui.json");
        Assert.Equal(HttpStatusCode.OK, document.Status);
        Assert.StartsWith("3.0", document.Json["openapi"]!.GetValue<string>());
        var paths = document.Json["paths"]!.AsObject();
        Assert.All(paths.Select(p => p.Key), path => Assert.StartsWith("/api/ui", path));

        var twins = _api.App.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(a => a.MethodInfo.GetCustomAttribute<UiApiAttribute>() != null)
            .ToList();
        Assert.True(twins.Count > 150, "the UI API actions were not found: " + twins.Count);

        var missing = new List<string>();
        foreach (var action in twins)
        {
            var path = "/" + ProtocolsDocumentTransformer.CleanPath(action.AttributeRouteInfo!.Template!);
            var methods = action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).Distinct().ToArray() ?? new[] { "GET" };
            foreach (var method in methods)
            {
                if (paths[path]?[method.ToLowerInvariant()] == null) missing.Add($"{method} {path} ({action.DisplayName})");
            }
        }

        Assert.True(missing.Count == 0, "UI API operations missing from the ui document:" + Environment.NewLine + string.Join(Environment.NewLine, missing));

        // Both variants of a repository screen.
        Assert.NotNull(paths["/api/ui/stats"]?["get"]);
        Assert.NotNull(paths["/api/ui/r/{repo}/stats"]?["get"]);

        // A screen result is the answer.
        Assert.Contains("ScreenResult", paths["/api/ui/stats"]!["get"]!["responses"]!["200"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSecuritySchemesOfTheDocumentsAreTheCredentialsTheirApisAccept()
    {
        static string[] Schemes(JsonNode document) => document["components"]!["securitySchemes"]!.AsObject().Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        // /api/ui: API keys and session tokens in headers a page cannot make a browser send. No cookie scheme (the cookie needs an antiforgery header), no HTTP Basic.
        var ui = (await _api.GetAsync("/openapi/ui.json")).Json;
        Assert.Equal(new[] { "apiKey", "bearer", "session" }, Schemes(ui));
        Assert.Equal(new[] { "apiKey", "bearer", "session" }, ui["security"]!.AsArray().SelectMany(r => r!.AsObject().Select(p => p.Key)).OrderBy(k => k, StringComparer.Ordinal).ToArray());

        // /api/v1 never accepted the cookie either.
        var v1 = (await _api.GetAsync("/openapi/v1.json")).Json;
        Assert.Equal(new[] { "apiKey", "basic", "bearer" }, Schemes(v1));
        Assert.DoesNotContain("cookie", (await _api.GetAsync("/openapi/protocols.json")).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AScreenOnlyTwinSaysSoInItsDescriptionAndAnAdministrationOneDoesNot()
    {
        var paths = (await _api.GetAsync("/openapi/ui.json")).Json["paths"]!.AsObject();

        Assert.Contains("Interactive session only", paths["/api/ui/account/settings"]!["get"]!["description"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("API keys are refused", paths["/api/ui/packages/{id}/manage"]!["get"]!["description"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Null(paths["/api/ui/admin/users"]!["get"]!["description"]);
        Assert.Null(paths["/api/ui/stats"]!["get"]!["description"]);
    }

    [Fact]
    public async Task AnUnknownDocumentIsNotFoundEvenForAnAdministrator()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync("/openapi/nope.json")).Status);
    }

    [Fact]
    public async Task NoDocumentCarriesASecret()
    {
        foreach (var name in ApiDocuments.All)
        {
            var body = (await _api.GetAsync(ApiDocuments.JsonPath(name))).Body;
            Assert.DoesNotContain(AdminApiHarness.GlobalKey, body);
            Assert.DoesNotContain(_api.RootKey, body);
        }
    }

    [Fact]
    public async Task AdministratorKeysAndTheGlobalKeyReadTheDocuments()
    {
        foreach (var name in ApiDocuments.All)
        {
            Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync(ApiDocuments.JsonPath(name))).Status);
            Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync(ApiDocuments.JsonPath(name), AdminApiHarness.GlobalKey)).Status);
        }

        // The key as the Basic password, like the rest of the server.
        Assert.Equal(HttpStatusCode.OK, (await _api.SendAsync(HttpMethod.Get, "/openapi/v1.json", bearer: false)).Status);
    }

    [Fact]
    public async Task AnAdministratorsCookieSessionReadsTheDocumentsAndScalar()
    {
        using var root = new Browser(_api.App);
        await SignInAsync(root, "root");

        foreach (var name in ApiDocuments.All)
        {
            using var answer = await root.GetAsync(ApiDocuments.JsonPath(name));
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            JsonNode.Parse(await answer.Content.ReadAsStringAsync(AdminApiHarness.Token));
        }

        using var alias = await root.GetAsync("/api/v1/openapi.json");
        Assert.Equal(HttpStatusCode.OK, alias.StatusCode);

        using var scalar = await root.GetAsync("/scalar/v1");
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
        var html = await scalar.Content.ReadAsStringAsync(AdminApiHarness.Token);
        Assert.Contains("BaGet API", html);

        // The viewer's own scripts sit behind the same gate.
        using var script = await root.GetAsync("/scalar/scalar.aspnetcore.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        using var anonymousScript = await _api.Client.GetAsync("/scalar/scalar.aspnetcore.js", AdminApiHarness.Token);
        Assert.Equal(HttpStatusCode.NotFound, anonymousScript.StatusCode);
    }

    [Fact]
    public async Task EverybodyElseGets404()
    {
        var addresses = ApiDocuments.All.Select(ApiDocuments.JsonPath).Concat(new[] { "/api/v1/openapi.json", "/scalar", "/scalar/v1", "/scalar/protocols" }).ToList();

        // Anonymous, a bad key, a user's cookie, a user's key, an administrator's key without the admin scope or limited to repositories.
        using var alice = new Browser(_api.App);
        await SignInAsync(alice, "alice");
        var packageKey = await _api.KeyAsync("root", ApiKeyScopes.All);
        var limitedKey = await _api.KeyAsync("root", repository: Repository.DefaultName);
        var aliceKey = await _api.KeyAsync("alice", ApiKeyScopes.All);

        foreach (var address in addresses)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync(address, key: "")).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync(address, "not-a-key")).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync(address, packageKey)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync(address, limitedKey)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync(address, aliceKey)).Status);

            using var cookie = await alice.GetAsync(address);
            Assert.Equal(HttpStatusCode.NotFound, cookie.StatusCode);
        }
    }

    [Fact]
    public async Task AnOpenServerWithNoGlobalKeyShowsTheDocumentsToNobodyAnonymous()
    {
        using var open = new BaGetApplication(_output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "false" } });
        using var client = open.CreateClient();

        foreach (var name in ApiDocuments.All)
        {
            using var answer = await client.GetAsync(ApiDocuments.JsonPath(name), AdminApiHarness.Token);
            Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        }

        using var scalar = await client.GetAsync("/scalar", AdminApiHarness.Token);
        Assert.Equal(HttpStatusCode.NotFound, scalar.StatusCode);
    }
}
