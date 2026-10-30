using System;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.V3.Api;

namespace BaGet.Web.V3.Controllers.Base;

/// <summary>A change that needs the relational access model; the Azure Table database has none.</summary>
public abstract class AccessApiController : AdminApiController
{
    /// <summary>The access model's administration.</summary>
    protected IAccessAdministration AccessAdmin => Service<IAccessAdministration>();

    /// <summary>422 where the database has no access model, else null.</summary>
    protected IActionResult? Unavailable()
        => Service<IContext>() is NullContext
            ? ApiProblems.Create(422, "not-supported", "Not supported here", "This database has no access model (Azure Table): everyone reads and any authenticated caller writes.")
            : null;

    /// <summary>The principal type named by <paramref name="text"/>, or null with the problem to send.</summary>
    protected bool TryPrincipalType(string? text, out PrincipalType type, out IActionResult? problem)
    {
        problem = null;
        if (Enum.TryParse(text, ignoreCase: true, out type) && Enum.IsDefined(type)) return true;

        problem = ApiProblems.BadRequest($"Unknown principal type '{text}'. Use User, Group or Organization.");

        return false;
    }

    /// <summary>The id of the named principal, or throws the refusal the pages show.</summary>
    protected async Task<string> ResolveAsync(PrincipalType type, string? name)
        => await AccessAdmin.ResolvePrincipalAsync(type, name ?? string.Empty, Cancellation)
           ?? throw new AccessModelException($"There is no {type.ToString().ToLowerInvariant()} named '{name}'.");
}
