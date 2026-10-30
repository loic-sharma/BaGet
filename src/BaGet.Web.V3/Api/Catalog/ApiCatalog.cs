using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using BaGet.Web.V3.Models.Api;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Api.Catalog;

/// <summary>
/// What the administration API offers, read from its controllers: one <see cref="ApiEndpoint"/> per action. The <c>/admin/api</c> page and
/// the UI-coverage test read this one list, so they cannot differ from the code. The served OpenAPI document is built by
/// <c>Microsoft.AspNetCore.OpenApi</c> (<see cref="BaGet.Web.V3.OpenApi.ApiDocuments"/>); <see cref="OpenApi"/> is kept only for source compatibility.
/// </summary>
public static class ApiCatalog
{
    /// <summary>The path every operation lives under.</summary>
    public const string Root = "/api/v1";

    /// <summary>The same without the leading slash, for <c>[Route]</c> templates.</summary>
    public const string RootTemplate = "api/v1";

    /// <summary>The operations of every controller derived from <see cref="AdminApiController"/>, ordered by path then method.</summary>
    public static IReadOnlyList<ApiEndpoint> Endpoints(IActionDescriptorCollectionProvider provider)
    {
        var endpoints = new List<ApiEndpoint>();
        foreach (var action in provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            if (!typeof(AdminApiController).IsAssignableFrom(action.ControllerTypeInfo)) continue;

            var template = action.AttributeRouteInfo?.Template;
            var operation = action.MethodInfo.GetCustomAttribute<ApiOperationAttribute>();
            if (template == null || operation == null) continue;

            var methods = action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).Distinct().ToList() ?? new List<string>();
            foreach (var method in methods)
            {
                endpoints.Add(new ApiEndpoint(method, "/" + CleanPath(template), action.ControllerName, operation, action));
            }
        }

        return endpoints.OrderBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.Method, StringComparer.Ordinal).ToList();
    }

    /// <summary>Actions of the API controllers that have no <see cref="ApiOperationAttribute"/> or no HTTP method (a test fails for those).</summary>
    public static IReadOnlyList<string> Undescribed(IActionDescriptorCollectionProvider provider)
        => provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(a => typeof(AdminApiController).IsAssignableFrom(a.ControllerTypeInfo))
            .Where(a => a.MethodInfo.GetCustomAttribute<ApiOperationAttribute>() == null || a.AttributeRouteInfo?.Template == null)
            .Select(a => a.ControllerTypeInfo.Name + "." + a.MethodInfo.Name)
            .ToList();

    private static string CleanPath(string template)
        => Regex.Replace(template.Trim('/'), @"\{\*{0,2}([^}:?]+)[^}]*\}", "{$1}");

    internal static IReadOnlyList<ApiParameter> ParametersOf(ApiEndpoint endpoint)
    {
        var result = new List<ApiParameter>();
        foreach (var parameter in endpoint.Descriptor.MethodInfo.GetParameters())
        {
            if (parameter.ParameterType == typeof(System.Threading.CancellationToken) || parameter.Name == null) continue;

            var source = endpoint.Descriptor.Parameters.FirstOrDefault(p => p.Name == parameter.Name)?.BindingInfo?.BindingSource;
            var name = parameter.Name;
            var inPath = endpoint.Path.Contains("{" + name + "}", StringComparison.Ordinal);
            string location;
            if (source == BindingSource.Body) location = "body";
            else if (inPath) location = "path";
            else if (source == BindingSource.Query || IsSimple(parameter.ParameterType)) location = "query";
            else location = "body";

            var required = location == "path" || (location == "body" && !IsNullable(parameter));
            result.Add(new ApiParameter(location == "query" ? NameOnWire(parameter) : name, location, parameter.ParameterType, required));
        }

        return result;
    }

    private static string NameOnWire(ParameterInfo parameter)
        => parameter.GetCustomAttribute<Microsoft.AspNetCore.Mvc.FromQueryAttribute>()?.Name ?? parameter.Name!;

    private static bool IsNullable(ParameterInfo parameter)
        => parameter.HasDefaultValue || Nullable.GetUnderlyingType(parameter.ParameterType) != null
           || new NullabilityInfoContext().Create(parameter).WriteState == NullabilityState.Nullable;

    internal static bool IsSimple(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime)
               || type == typeof(DateTimeOffset) || type == typeof(Guid);
    }

    /// <summary>The OpenAPI 3.0 document of the API, written by the former in-house generator.</summary>
    /// <remarks>Obsolete: the server serves the document built by <c>Microsoft.AspNetCore.OpenApi</c> at <c>/openapi/v1.json</c>; nothing in the server calls this.</remarks>
    /// <param name="endpoints">The operations (<see cref="Endpoints"/>).</param>
    /// <param name="serverUrl">The address the API is served at (<c>https://host/base</c>), or null to leave <c>servers</c> out.</param>
    /// <param name="version">The version of the document.</param>
    [Obsolete("The server serves the Microsoft.AspNetCore.OpenApi document at /openapi/v1.json; this in-house generator is no longer used.")]
    public static JsonObject OpenApi(IReadOnlyList<ApiEndpoint> endpoints, string? serverUrl, string version)
    {
        var schemas = new JsonObject();
        var paths = new JsonObject();

        foreach (var group in endpoints.GroupBy(e => e.Path))
        {
            var item = new JsonObject();
            foreach (var endpoint in group)
            {
                item[endpoint.Method.ToLowerInvariant()] = Operation(endpoint, schemas);
            }

            paths[group.Key] = item;
        }

        schemas["Problem"] = new JsonObject
        {
            ["type"] = "object",
            ["description"] = "RFC 9457 problem details; 'code' is the stable last part of 'type'.",
            ["properties"] = new JsonObject
            {
                ["type"] = new JsonObject { ["type"] = "string" },
                ["title"] = new JsonObject { ["type"] = "string" },
                ["status"] = new JsonObject { ["type"] = "integer" },
                ["detail"] = new JsonObject { ["type"] = "string" },
                ["code"] = new JsonObject { ["type"] = "string" },
            },
        };

        var document = new JsonObject
        {
            ["openapi"] = "3.0.3",
            ["info"] = new JsonObject
            {
                ["title"] = "BaGet administration API",
                ["version"] = version,
                ["description"] = "Administers repositories, projects, users, groups, permission targets, retention, scanning and operations. "
                    + "Send an administrator's API key with the admin:api scope as 'Authorization: Bearer <key>' (or the server's global ApiKey). "
                    + "Operations marked x-baget-confirm also need the key owner's password in the X-Confirm-Password header.",
            },
            ["paths"] = paths,
            ["components"] = new JsonObject
            {
                ["securitySchemes"] = new JsonObject
                {
                    ["apiKey"] = new JsonObject { ["type"] = "http", ["scheme"] = "bearer", ["description"] = "An API key." },
                },
                ["schemas"] = schemas,
            },
            ["security"] = new JsonArray(new JsonObject { ["apiKey"] = new JsonArray() }),
        };
        if (!string.IsNullOrEmpty(serverUrl)) document["servers"] = new JsonArray(new JsonObject { ["url"] = serverUrl });

        return document;
    }

    private static JsonObject Operation(ApiEndpoint endpoint, JsonObject schemas)
    {
        var parameters = new JsonArray();
        JsonObject? body = null;
        foreach (var parameter in endpoint.Parameters)
        {
            if (parameter.In == "body")
            {
                body = new JsonObject
                {
                    ["required"] = parameter.Required,
                    ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = Schema(parameter.Type, schemas) } },
                };
                continue;
            }

            parameters.Add(new JsonObject
            {
                ["name"] = parameter.Name,
                ["in"] = parameter.In,
                ["required"] = parameter.Required,
                ["schema"] = Schema(parameter.Type, schemas),
            });
        }

        if (endpoint.Operation.Confirm)
        {
            parameters.Add(new JsonObject
            {
                ["name"] = AdminApiController.ConfirmHeader,
                ["in"] = "header",
                ["required"] = false,
                ["description"] = "The key owner's password (not needed with the global key).",
                ["schema"] = new JsonObject { ["type"] = "string" },
            });
        }

        var responses = new JsonObject();
        var success = new JsonObject { ["description"] = "Success" };
        if (endpoint.Operation.Response != null)
        {
            var type = endpoint.Operation.PagedList ? typeof(Paged<>).MakeGenericType(endpoint.Operation.Response) : endpoint.Operation.Response;
            success["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = Schema(type, schemas) } };
        }

        responses[endpoint.Operation.Status.ToString(System.Globalization.CultureInfo.InvariantCulture)] = success;
        foreach (var (status, text) in new[] { (400, "Invalid request"), (401, "No valid key"), (403, "Not an administrator, or confirmation missing"), (404, "Not found"), (409, "Conflict"), (503, "The server is read-only") })
        {
            responses[status.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["description"] = text,
                ["content"] = new JsonObject
                {
                    ["application/problem+json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/Problem" } },
                },
            };
        }

        var operation = new JsonObject
        {
            ["operationId"] = endpoint.Descriptor.ControllerName + "_" + endpoint.Descriptor.ActionName,
            ["summary"] = endpoint.Operation.Summary,
            ["tags"] = new JsonArray(endpoint.Tag),
            ["parameters"] = parameters,
            ["responses"] = responses,
            ["x-baget-ui-screen"] = endpoint.Operation.UiScreen.Controller + "/" + endpoint.Operation.UiScreen.Action,
            ["x-baget-confirm"] = endpoint.Operation.Confirm,
        };
        if (body != null) operation["requestBody"] = body;

        return operation;
    }

    internal static JsonNode Schema(Type type, JsonObject schemas)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string)) return new JsonObject { ["type"] = "string" };
        if (type == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (type == typeof(int) || type == typeof(short) || type == typeof(byte)) return new JsonObject { ["type"] = "integer", ["format"] = "int32" };
        if (type == typeof(long)) return new JsonObject { ["type"] = "integer", ["format"] = "int64" };
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return new JsonObject { ["type"] = "number" };
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return new JsonObject { ["type"] = "string", ["format"] = "date-time" };
        if (type == typeof(Guid)) return new JsonObject { ["type"] = "string", ["format"] = "uuid" };
        if (type == typeof(object)) return new JsonObject();

        if (type.IsEnum)
        {
            return new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Enum.GetNames(type).Select(n => (JsonNode)JsonValue.Create(n)!).ToArray()) };
        }

        var dictionary = type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictionary != null)
        {
            return new JsonObject { ["type"] = "object", ["additionalProperties"] = Schema(dictionary.GetGenericArguments()[1], schemas) };
        }

        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
        {
            var element = type.IsArray
                ? type.GetElementType()!
                : type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0] ?? typeof(object);

            return new JsonObject { ["type"] = "array", ["items"] = Schema(element, schemas) };
        }

        var name = SchemaName(type);
        if (!schemas.ContainsKey(name))
        {
            schemas[name] = new JsonObject(); // reserves the name, so a type that refers to itself terminates
            var properties = new JsonObject();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead || property.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

                properties[System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = Schema(property.PropertyType, schemas);
            }

            schemas[name] = new JsonObject { ["type"] = "object", ["properties"] = properties };
        }

        return new JsonObject { ["$ref"] = "#/components/schemas/" + name };
    }

    private static string SchemaName(Type type)
    {
        if (!type.IsGenericType) return type.Name;

        var name = type.Name.Substring(0, type.Name.IndexOf('`'));

        return name + string.Concat(type.GetGenericArguments().Select(SchemaName));
    }
}
