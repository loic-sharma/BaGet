using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Web.V3.Filters;

/// <summary>
/// Refuses the action with <c>403</c> when the request is for a remote repository: a remote repository holds what the
/// server cached from its upstream, so clients cannot deploy to it (the access model never grants Deploy there either;
/// this also covers hosts without accounts, where the caller's API key is the only check).
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method)]
public sealed class RemoteRepositoryReadOnlyAttribute : ActionFilterAttribute
{
    /// <inheritdoc/>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var repository = context.HttpContext.RequestServices.GetService<IRepositoryContext>()?.Current;
        if (repository?.Kind == RepositoryKind.Remote)
        {
            context.Result = new ObjectResult("This is a remote repository: it caches its upstream and takes no uploads.")
            {
                StatusCode = 403,
            };
        }
    }
}
