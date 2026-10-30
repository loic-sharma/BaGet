using System;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Identity.Extensions;

/// <summary>
/// Links to a repository's pages that keep the package ID or user name exactly as written. <c>Url.Action</c> lowercases
/// the whole path (<c>RouteOptions.LowercaseUrls</c>), which would turn <c>Contoso.Lib</c> into <c>contoso.lib</c> in
/// every link and in the heading of the page it opens; these keep the case and add the <c>r/{name}/</c> prefix of the
/// repository instead.
/// </summary>
public static class RepositoryLinks
{
    /// <summary>
    /// <c>~/{path}</c> in the repository of the current request (<c>~/r/{name}/{path}</c> for a non-default one).
    /// </summary>
    public static string RepositoryContent(this IUrlHelper url, string path)
    {
        if (url == null) throw new ArgumentNullException(nameof(url));

        var repository = url.ActionContext.HttpContext.RequestServices.GetService<IRepositoryContext>()?.Current
            ?? RepositoryInfo.Default;

        return url.RepositoryContent(repository, path);
    }

    /// <summary>
    /// <c>~/{path}</c> in <paramref name="repository"/>.
    /// </summary>
    public static string RepositoryContent(this IUrlHelper url, RepositoryInfo repository, string path)
    {
        if (url == null) throw new ArgumentNullException(nameof(url));
        if (repository == null) throw new ArgumentNullException(nameof(repository));

        return url.Content("~/" + repository.PathPrefix + (path ?? string.Empty).TrimStart('/')) ?? string.Empty;
    }
}
