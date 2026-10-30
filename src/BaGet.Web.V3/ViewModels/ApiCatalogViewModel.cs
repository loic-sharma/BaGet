using System.Collections.Generic;

namespace BaGet.Web.V3.ViewModels;

/// <summary>What the REST API page shows (also the model of its JSON twin).</summary>
/// <param name="Endpoints">Every operation, ordered by path.</param>
/// <param name="ApiBase">The address of the API on this server.</param>
/// <param name="ConfirmHeader">The header that carries the password of the owner.</param>
public sealed record ApiCatalogViewModel(IReadOnlyList<ApiEndpointSummary> Endpoints, string ApiBase, string ConfirmHeader)
{
    /// <summary>Where the OpenAPI document is.</summary>
    public string OpenApiUrl => ApiBase + "/openapi.json";
}
