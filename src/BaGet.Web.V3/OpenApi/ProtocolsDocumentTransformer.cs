using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;
using BaGet.Web.V3.Routing;

namespace BaGet.Web.V3.OpenApi;

/// <summary>
/// Fills the <c>protocols</c> document from the mapped endpoints. The package protocols are conventionally routed
/// (<see cref="BaGetEndpointBuilder.MapRoute"/>), which ApiExplorer does not see, so every endpoint carrying
/// <see cref="RepositoryRouteMetadata"/> becomes an operation per HTTP method: its template (constraints removed, plain and
/// <c>r/{repo}/</c> variants), route parameters, the format and the controller action. Request and response bodies are not described
/// (the protocols are defined by their own specifications).
/// </summary>
internal sealed class ProtocolsDocumentTransformer : IOpenApiDocumentTransformer
{
    private static readonly string[] AnyMethod = { "GET" };

    private readonly EndpointDataSource _endpoints;

    public ProtocolsDocumentTransformer(EndpointDataSource endpoints)
    {
        _endpoints = endpoints;
    }

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Paths ??= new OpenApiPaths();
        var tags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var endpoint in _endpoints.Endpoints.OfType<RouteEndpoint>().OrderBy(e => e.RoutePattern.RawText, StringComparer.Ordinal))
        {
            var metadata = endpoint.Metadata.GetMetadata<RepositoryRouteMetadata>();
            var raw = endpoint.RoutePattern.RawText;
            if (metadata == null || string.IsNullOrEmpty(raw)) continue;

            var path = "/" + CleanPath(raw);
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? AnyMethod;
            var parameters = endpoint.RoutePattern.Parameters.Select(p => p.Name).Distinct(StringComparer.Ordinal).ToList();
            var format = metadata.Format.ToString();
            tags.Add(format);

            OpenApiPathItem pathItem;
            if (document.Paths.TryGetValue(path, out var existing) && existing is OpenApiPathItem found)
            {
                pathItem = found;
            }
            else
            {
                pathItem = new OpenApiPathItem();
                document.Paths[path] = pathItem;
            }

            pathItem.Operations ??= new Dictionary<HttpMethod, OpenApiOperation>();
            foreach (var method in methods.Select(m => m.ToUpperInvariant()).Distinct(StringComparer.Ordinal))
            {
                var httpMethod = new HttpMethod(method);
                if (pathItem.Operations.ContainsKey(httpMethod)) continue;

                var name = action == null ? raw : action.ControllerName + "." + action.ActionName;
                var operation = new OpenApiOperation
                {
                    Summary = $"{name} ({format})",
                    OperationId = OperationId(method, path),
                    Tags = new HashSet<OpenApiTagReference> { new(format, document) },
                    Parameters = parameters.Select(p => (IOpenApiParameter)new OpenApiParameter
                    {
                        Name = p,
                        In = ParameterLocation.Path,
                        Required = true,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    }).ToList(),
                    Responses = new OpenApiResponses { ["default"] = new OpenApiResponse { Description = "The answer defined by the " + format + " protocol." } },
                    Extensions = new Dictionary<string, IOpenApiExtension>
                    {
                        ["x-baget-format"] = new JsonNodeExtension(JsonValue.Create(format)!),
                        ["x-baget-writes-only"] = new JsonNodeExtension(JsonValue.Create(metadata.WritesOnly)),
                    },
                };
                pathItem.Operations[httpMethod] = operation;
            }
        }

        document.Tags ??= new HashSet<OpenApiTag>();
        foreach (var tag in tags.OrderBy(t => t, StringComparer.Ordinal)) document.Tags.Add(new OpenApiTag { Name = tag });

        return Task.CompletedTask;
    }

    /// <summary>The template as OpenAPI writes it: constraints, defaults and the optional mark dropped, a catch-all as a plain parameter.</summary>
    internal static string CleanPath(string template)
        => Regex.Replace(template.Trim('/'), @"\{\*{0,2}([^}:?=]+)[^}]*\}", "{$1}");

    private static string OperationId(string method, string path)
        => method.ToLowerInvariant() + "_" + Regex.Replace(path.Trim('/'), @"[^A-Za-z0-9]+", "_").Trim('_');
}
