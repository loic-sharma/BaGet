using System;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Controllers.Api;

/// <summary>
/// Obsolete and no longer routed (<see cref="NonControllerAttribute"/>): the document at <c>/api/v1/openapi.json</c> is now the
/// <c>v1</c> document of <c>Microsoft.AspNetCore.OpenApi</c> (<see cref="BaGet.Web.V3.OpenApi.ApiDocuments"/>), served by
/// <c>BaGetEndpointBuilder.MapApiDocumentation</c> to administrators. The type stays so existing references still compile.
/// </summary>
[NonController]
[Obsolete("The OpenAPI document is served by BaGetEndpointBuilder.MapApiDocumentation (/openapi/v1.json, alias /api/v1/openapi.json).")]
public class OpenApiController : AdminApiController
{
    /// <summary>The version of the API (the path says it too).</summary>
    public const string ApiVersion = "1.0.0";

    private readonly IActionDescriptorCollectionProvider _actions;

    /// <summary>Creates the controller.</summary>
    public OpenApiController(IActionDescriptorCollectionProvider actions)
    {
        _actions = actions;
    }

    /// <summary>The document.</summary>
    [NonAction]
    public IActionResult Document()
    {
        var root = Service<IUrlGenerator>().GetServiceIndexUrl() ?? string.Empty;
        const string index = "v3/index.json";
        var server = root.EndsWith(index, System.StringComparison.Ordinal) ? root.Substring(0, root.Length - index.Length) : root.TrimEnd('/') + "/";
        #pragma warning disable CS0618
        var document = ApiCatalog.OpenApi(ApiCatalog.Endpoints(_actions), server.TrimEnd('/'), ApiVersion);
#pragma warning restore CS0618

        return new ContentResult { Content = document.ToJsonString(), ContentType = "application/json", StatusCode = 200 };
    }
}
