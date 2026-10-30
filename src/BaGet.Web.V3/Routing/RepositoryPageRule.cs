using BaGet.Core;
using BaGet.Web.V3.Filters;

namespace BaGet.Web.V3.Routing;

/// <summary>
/// What a repository page requires of the repository it is served in. Built from the attribute of a controller action
/// (<c>BaGet.Identity.Mvc.Attributes.RepositoryPageAttribute</c>, <see cref="RepositoryActionFilter"/>) and judged by <see cref="RepositoryPageGate"/>.
/// </summary>
/// <param name="Format">The format the page belongs to, or null when it serves every format.</param>
/// <param name="Upload">The caller needs the right to publish rather than Read, and a virtual repository continues in its deploy repository.</param>
/// <param name="NotOnVirtual">404 in a virtual repository.</param>
/// <param name="NotOnRemote">404 in a remote repository.</param>
/// <param name="FrontPage">The NuGet front page: in a repository of another format it redirects to the format's browse link.</param>
internal sealed record RepositoryPageRule(RepositoryFormat? Format, bool Upload, bool NotOnVirtual, bool NotOnRemote, bool FrontPage);
