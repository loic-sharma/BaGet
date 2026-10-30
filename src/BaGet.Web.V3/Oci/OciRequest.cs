using BaGet.Core;
using Microsoft.AspNetCore.Http;
using BaGet.Web.V3.Controllers.Protocols;

namespace BaGet.Web.V3.Oci;

/// <summary>What the authorization filter worked out for a request, handed to <see cref="OciController"/>.</summary>
/// <param name="Path">The parsed path: kind, image and reference.</param>
/// <param name="Repository">The BaGet repository the client addressed (its name is in every URL the registry hands out).</param>
/// <param name="Caller">Who is asking.</param>
/// <param name="Target">The repository the request is authorized in and runs in: <paramref name="Repository"/>, except for a push or delete to a virtual repository, which is its deploy repository's.</param>
public sealed record OciRequest(OciPath Path, RepositoryInfo Repository, OciCaller Caller, RepositoryInfo Target)
{
    /// <summary><see cref="HttpContext.Items"/> key.</summary>
    public const string ItemKey = "BaGet.OciRequest";
}
