using System;
using BaGet.Core;
using Microsoft.AspNetCore.Http;

namespace BaGet.Identity.Application.Screens;

/// <summary>
/// The feed address of a repository, as shown to administrators to paste into a client: the format's
/// <see cref="IFormatModule.FeedPath"/> (NuGet: <c>v3/index.json</c>) below the repository base, which is the root for the
/// default repository and <c>/r/{name}/</c> for the others. Built from the server's own service index URL (so a configured public URL or
/// path base is honoured), falling back to the address of the request being served.
/// </summary>
public sealed class RepositoryFeedUrl
{
    private const string ServiceIndexPath = "v3/index.json";

    private readonly IUrlGenerator? _urls;
    private readonly IHttpContextAccessor _http;

    /// <summary>Creates the service.</summary>
    public RepositoryFeedUrl(IHttpContextAccessor http, IUrlGenerator? urls = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _urls = urls;
    }

    /// <summary>The address of <paramref name="repositoryName"/>'s feed for a format whose feed path is <paramref name="feedPath"/>.</summary>
    public string For(string repositoryName, string feedPath = ServiceIndexPath)
        => feedPath.Contains("{repo}", StringComparison.Ordinal)
            ? BaseFor(Repository.DefaultName) + feedPath.Replace("{repo}", repositoryName)
            : BaseFor(repositoryName) + feedPath;

    /// <summary>The address of the repository's base (ends with a slash): where its format's routes start.</summary>
    public string BaseFor(string repositoryName)
    {
        var root = _urls?.GetServiceIndexUrl();
        if (string.IsNullOrEmpty(root))
        {
            var request = _http.HttpContext?.Request;
            root = request == null ? "/" + ServiceIndexPath : $"{request.Scheme}://{request.Host}{request.PathBase}/{ServiceIndexPath}";
        }

        var prefix = root.EndsWith(ServiceIndexPath, StringComparison.Ordinal) ? root.Substring(0, root.Length - ServiceIndexPath.Length) : root.TrimEnd('/') + "/";

        return repositoryName == Repository.DefaultName ? prefix : $"{prefix}r/{repositoryName}/";
    }
}
