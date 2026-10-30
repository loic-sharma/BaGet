using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace BaGet.Web.V3.Api.Catalog;

/// <summary>One operation of the administration API, as read from the controllers.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path template, constraints removed (<c>/api/v1/groups/{name}</c>).</param>
/// <param name="Tag">The resource the controller serves.</param>
/// <param name="Operation">The <see cref="ApiOperationAttribute"/> of the action.</param>
/// <param name="Descriptor">The action.</param>
public sealed record ApiEndpoint(string Method, string Path, string Tag, ApiOperationAttribute Operation, ControllerActionDescriptor Descriptor)
{
    /// <summary>The query parameters, route parameters and body of the action, in declaration order.</summary>
    public IReadOnlyList<ApiParameter> Parameters => ApiCatalog.ParametersOf(this);
}
