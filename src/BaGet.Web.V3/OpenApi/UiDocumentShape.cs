using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BaGet.Web.V3.OpenApi;

/// <summary>
/// What the <c>ui</c> document must say so that a client generated from it matches the JSON the API writes (<c>UiApiJson</c>): enums are the names they are
/// written as, the error answers have the type of the 200 answer (every status carries a <c>ScreenResult&lt;T&gt;</c>), and an optional route value is a path
/// of its own (<c>packages/{id}</c> and <c>packages/{id}/{version}</c>), not a query parameter hiding behind a path template that mentions it.
/// </summary>
internal static class UiDocumentShape
{
    private static readonly (string Code, string Description)[] ErrorAnswers =
    {
        ("400", "Invalid: the input was refused. The body still holds the screen's model and the errors by field (\"\" = the whole form)."),
        ("401", "SignIn: the caller must authenticate (a session token, an API key, or the cookie with the antiforgery header). Carries WWW-Authenticate: Bearer."),
        ("403", "Forbidden: the caller may not do this."),
        ("404", "NotFound: no such screen or item, or the caller may not know it exists."),
    };

    /// <summary>An enum is a string schema listing its names (the API serialises enums by name).</summary>
    public static Task EnumsAsNames(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!type.IsEnum) return Task.CompletedTask;

        var nullable = schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null);
        schema.Type = nullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
        schema.Format = null;
        schema.Pattern = null;
        schema.AnyOf = null;
        schema.Enum = Enum.GetNames(type).Select(name => (JsonNode)JsonValue.Create(name)!).ToList();

        return Task.CompletedTask;
    }

    /// <summary>An integer is an integer: .NET's default number handling publishes it as "integer or string matching a pattern", which the API never writes.</summary>
    public static Task IntegersAsIntegers(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // The number handling is applied to the properties after the schema transformers ran, so the finished schemas are corrected.
        foreach (var schema in document.Components?.Schemas?.Values ?? Enumerable.Empty<IOpenApiSchema>()) FixIntegers(schema, depth: 0);

        return Task.CompletedTask;
    }

    private static void FixIntegers(IOpenApiSchema? candidate, int depth)
    {
        if (candidate is not OpenApiSchema schema || depth > 4) return;

        // (the type is a set of flags: integer, string and perhaps null)
        if (schema.Pattern == @"^-?(?:0|[1-9]\d*)$" && schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Integer))
        {
            schema.Type = schema.Type.Value.HasFlag(JsonSchemaType.Null) ? JsonSchemaType.Integer | JsonSchemaType.Null : JsonSchemaType.Integer;
            schema.AnyOf = null;
            schema.Pattern = null;
        }

        if (schema.Properties != null) foreach (var property in schema.Properties.Values) FixIntegers(property, depth + 1);
        FixIntegers(schema.Items, depth + 1);
        FixIntegers(schema.AdditionalProperties, depth + 1);
    }

    /// <summary>The 400, 401, 403 and 404 answers of an operation that answers a <c>ScreenResult</c> are described with the schema of its 200 answer.</summary>
    public static Task ErrorAnswersLikeTheOkAnswer(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.Responses == null
            || !operation.Responses.TryGetValue("200", out var ok)
            || ok.Content == null
            || !ok.Content.TryGetValue("application/json", out var json)
            || json.Schema is not OpenApiSchemaReference { Reference.Id: { } id }
            || !id.StartsWith("ScreenResult", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        foreach (var (code, description) in ErrorAnswers)
        {
            if (operation.Responses.ContainsKey(code)) continue;

            operation.Responses[code] = new OpenApiResponse
            {
                Description = description,
                Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = json.Schema } },
            };
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// ApiExplorer publishes <c>packages/{id}/{version?}</c> as the path <c>packages/{id}/{version}</c> with <c>version</c> a query parameter. The path is split in two:
    /// the one without the segment, and the one with it, where the value is a required path parameter.
    /// </summary>
    public static Task OptionalRouteValuesAsPaths(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Paths == null) return Task.CompletedTask;

        foreach (var (path, item) in document.Paths.ToList())
        {
            if (item.Operations == null) continue;

            var optional = Regex.Matches(path, @"\{([^}/]+)\}").Select(m => m.Groups[1].Value)
                .Where(name => item.Operations.Values.All(op => op.Parameters?.Any(p => p.Name == name && p.In == ParameterLocation.Query) == true
                                                                  && op.Parameters.All(p => !(p.Name == name && p.In == ParameterLocation.Path))))
                .ToList();
            if (optional.Count == 0) continue;

            var shortPath = path;
            var shortItem = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() };
            foreach (var (method, operation) in item.Operations)
            {
                var copy = new OpenApiOperation(operation);
                copy.Parameters = operation.Parameters!.Where(p => !optional.Contains(p.Name!)).ToList();
                shortItem.Operations[method] = copy;
                if (operation.OperationId != null) copy.OperationId = operation.OperationId + "_without_" + string.Join("_", optional);

                // The full path: the value is part of it.
                operation.Parameters = operation.Parameters!.Select(p => optional.Contains(p.Name!) && p.In == ParameterLocation.Query
                    ? (IOpenApiParameter)new OpenApiParameter { Name = p.Name, In = ParameterLocation.Path, Required = true, Description = p.Description, Schema = p.Schema }
                    : p).ToList();
            }

            foreach (var name in optional) shortPath = shortPath.Replace("/{" + name + "}", string.Empty, StringComparison.Ordinal);
            if (!document.Paths.ContainsKey(shortPath)) document.Paths[shortPath] = shortItem;
        }

        return Task.CompletedTask;
    }
}
