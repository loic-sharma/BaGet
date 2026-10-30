using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.CompilerServices;
using BaGet.Frontend.Controllers.Base;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace BaGet.Web.V3.OpenApi;

/// <summary>
/// The OpenAPI documents of the server (<c>Microsoft.AspNetCore.OpenApi</c>), served at <c>/openapi/{name}.json</c> and browsed with
/// Scalar at <c>/scalar</c>, for administrators only (<see cref="ApiDocumentationAccess"/>).
/// </summary>
public static class ApiDocuments
{
    /// <summary>The screens' JSON twins, <c>/api/ui</c> (filled by the controllers under that prefix as they appear).</summary>
    public const string Ui = "ui";

    /// <summary>The administration REST API, <c>/api/v1</c>.</summary>
    public const string V1 = "v1";

    /// <summary>The package protocols (NuGet, npm, Maven, Docker), read from the mapped endpoints.</summary>
    public const string Protocols = "protocols";

    /// <summary>Every document name, in the order Scalar lists them.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Ui, V1, Protocols };

    /// <summary>The path prefix whose attribute-routed actions make up the <see cref="Ui"/> document.</summary>
    public const string UiPrefix = "api/ui";

    /// <summary>The path of the Scalar viewer.</summary>
    public const string ScalarPath = "/scalar";

    /// <summary>The path of the JSON of a document.</summary>
    public static string JsonPath(string name) => "/openapi/" + name + ".json";

    /// <summary>Registers the three documents (written as OpenAPI 3.0, which every client generator reads).</summary>
    public static IServiceCollection AddBaGetOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(Ui, options =>
        {
            // /api/ui accepts API keys and session tokens in headers a web page cannot make a browser send: no HTTP Basic, and the session cookie only with an antiforgery header.
            Configure(options, "BaGet UI API",
                "The JSON twin of every screen of the web UI (/api/ui). Authenticate with an API key as 'Authorization: Bearer <key>' or the X-NuGet-ApiKey header, or, for a person, "
                + "with a session token ('Authorization: Bearer bgs.xxx') from POST /api/ui/session (a single-page application on any origin). A same-origin client may use the browser cookie "
                + "if it sends the antiforgery token of GET /api/ui/antiforgery in the X-XSRF-TOKEN header on every request; the bare cookie and HTTP Basic are not accepted. "
                + "Administration screens need an administrator's key with the admin:api scope and no repository limit, or an administrator's interactive session (the global ApiKey is refused). "
                + "Account self-service and package ownership screens need an interactive session; API keys are refused with 403.",
                description => IsUnder(description, UiPrefix),
                basic: false,
                session: true);
            options.AddOperationTransformer(ScreenOnlyNote);
            options.AddSchemaTransformer(UiDocumentShape.EnumsAsNames);
            options.AddDocumentTransformer(UiDocumentShape.IntegersAsIntegers);
            options.AddOperationTransformer(UiDocumentShape.ErrorAnswersLikeTheOkAnswer);
            options.AddDocumentTransformer(UiDocumentShape.OptionalRouteValuesAsPaths);
        });
        services.AddOpenApi(V1, options => Configure(options, "BaGet administration API",
            "Administers repositories, projects, users, groups, permission targets, retention, scanning and operations. "
            + "Send an administrator's API key with the admin:api scope as 'Authorization: Bearer <key>' (or the server's global ApiKey). "
            + "Operations that need confirmation also take the key owner's password in the X-Confirm-Password header.",
            description => IsUnder(description, "api/v1")));
        services.AddOpenApi(Protocols, options =>
        {
            Configure(options, "BaGet package protocols",
                "The NuGet, symbol, npm, Maven and Docker (OCI) routes of the server. Every route exists at its plain path (the default repository) and under /r/{repo}/.",
                _ => false);
            options.AddDocumentTransformer<ProtocolsDocumentTransformer>();
        });

        return services;
    }

    /// <summary>The note added to the description of an operation whose screen is for interactive sessions only.</summary>
    public const string ScreenOnlyDescription = "Interactive session only (bearer session token or same-origin cookie with antiforgery header); API keys are refused. This screen changes or reveals credentials or ownership, so it answers 403 to an API key and to an anonymous caller.";

    private static Task ScreenOnlyNote(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor is ControllerActionDescriptor action && IsScreenOnly(action.ControllerTypeInfo))
        {
            operation.Description = string.IsNullOrEmpty(operation.Description) ? ScreenOnlyDescription : operation.Description + " " + ScreenOnlyDescription;
        }

        return Task.CompletedTask;
    }

    // The twin's base says it (AccountApiControllerBase.CookieOnly, a constant per type): read it without a request.
    internal static bool IsScreenOnly(Type controller)
    {
        if (!typeof(AccountApiControllerBase).IsAssignableFrom(controller)) return false;

        var property = typeof(AccountApiControllerBase).GetProperty("CookieOnly", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

        return (bool)property.GetValue(RuntimeHelpers.GetUninitializedObject(controller))!;
    }

    private static void Configure(OpenApiOptions options, string title, string description, Func<ApiDescription, bool> include, bool basic = true, bool session = false)
    {
        options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
        options.ShouldInclude = include;
        options.AddDocumentTransformer((document, context, _) =>
        {
            document.Info = new OpenApiInfo { Title = title, Version = "1.0.0", Description = description };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", Description = "An API key." };
            if (basic) document.Components.SecuritySchemes["basic"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "basic", Description = "The API key as the password." };
            document.Components.SecuritySchemes["apiKey"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-NuGet-ApiKey", Description = "An API key." };
            if (session)
            {
                document.Components.SecuritySchemes["session"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "bgs.xxx",
                    Description = "A session token: the accessToken of POST /api/ui/session, refreshed with POST /api/ui/session/refresh. Only /api/ui accepts it.",
                };
            }
            document.Security = new List<OpenApiSecurityRequirement>
            {
                new() { [new OpenApiSecuritySchemeReference("bearer", document)] = new List<string>() },
            };
            if (basic) document.Security.Add(new() { [new OpenApiSecuritySchemeReference("basic", document)] = new List<string>() });
            document.Security.Add(new() { [new OpenApiSecuritySchemeReference("apiKey", document)] = new List<string>() });
            if (session) document.Security.Add(new() { [new OpenApiSecuritySchemeReference("session", document)] = new List<string>() });

            return Task.CompletedTask;
        });
    }

    private static bool IsUnder(ApiDescription description, string prefix)
    {
        var path = description.RelativePath ?? string.Empty;

        return path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
