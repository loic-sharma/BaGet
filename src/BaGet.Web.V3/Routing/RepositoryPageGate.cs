using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Web.V3.Routing;

/// <summary>
/// The single check every repository page passes: the caller must be allowed to read the repository (the upload pages: to publish
/// to it) the request resolved to (<see cref="IRepositoryAccess"/>), else the page answers 404 to a signed-in user (the same
/// answer as an unknown repository, so nothing says that a private one exists) and sends a visitor who is not signed in to sign
/// in, when the host has accounts. A page whose route names a package (<c>{id}</c>, <c>{group}</c>/<c>{artifact}</c>,
/// <c>{image}</c>) is checked against that package too (<see cref="IRepositoryAccess.CanReadPackageAsync"/>).
/// </summary>
internal static class RepositoryPageGate
{
    /// <summary><see cref="HttpContext.Items"/> key holding the virtual repository the request asked for, when it was moved to its deploy repository (the upload pages).</summary>
    public const string RequestedVirtualItemKey = "BaGet.RequestedVirtualRepository";

    /// <summary>
    /// Judges a request for a repository page. Returns the result that answers it (404, a redirect, a challenge), or null when the
    /// page may run; the upload of a virtual repository leaves null after switching the request to the deploy repository.
    /// </summary>
    public static async Task<IActionResult?> CheckAsync(HttpContext httpContext, RouteData routeData, RepositoryPageRule rule)
    {
        var services = httpContext.RequestServices;
        var repository = services.GetRequiredService<IRepositoryContext>().Current;
        var access = services.GetRequiredService<IRepositoryAccess>();

        // A page of one package ({id} in its route) also needs Read on that package, so the include/exclude
        // patterns of the caller's grants hide it as they hide its registration and content.
        var packageId = routeData.Values.TryGetValue("id", out var id) ? id as string : null;
        if (repository.Format == RepositoryFormat.Npm && !string.IsNullOrEmpty(packageId))
        {
            // The access patterns see the decoded name (the patterns ignore case), so @acme%2fSecret cannot reach a page that
            // the pattern for @acme/secret hides.
            if (!NpmNames.TryParseLookup(packageId, out var npmName, out _))
            {
                return new NotFoundResult();
            }

            packageId = npmName;
        }

        if (repository.Format == RepositoryFormat.Maven
            && routeData.Values.TryGetValue("group", out var group) && group is string mavenGroup
            && routeData.Values.TryGetValue("artifact", out var artifact) && artifact is string mavenArtifact)
        {
            // A Maven page names its artifact by group and artifact; the access patterns see group:artifact.
            if (!MavenNames.IsValidGroupId(mavenGroup) || !MavenNames.IsValidArtifactId(mavenArtifact))
            {
                return new NotFoundResult();
            }

            packageId = MavenNames.ArtifactName(mavenGroup, mavenArtifact);
        }

        if (repository.Format == RepositoryFormat.Docker
            && routeData.Values.TryGetValue("image", out var image) && image is string dockerImage)
        {
            // A Docker page names its image in the address; the access patterns see the (lower-case) image name, as the registry routes do.
            if (!OciNames.IsValidImage(dockerImage))
            {
                return new NotFoundResult();
            }

            packageId = dockerImage;
        }

        // The upload pages show no package data of the repository, so they need the right to publish
        // (IRepositoryAccess.CanWriteAsync), not Read: a Deploy-only drop box is usable.
        var allowed = rule.Upload
            ? await access.CanWriteAsync(httpContext.User, repository, httpContext.RequestAborted)
            : string.IsNullOrEmpty(packageId)
            ? await access.CanReadAsync(httpContext.User, repository, httpContext.RequestAborted)
            : await access.CanReadPackageAsync(httpContext.User, repository, packageId, httpContext.RequestAborted);

        if (allowed && rule.Format is { } pageFormat && repository.Format != pageFormat)
        {
            // The page belongs to another format. The front page of a repository is where its address (/r/name/) and the
            // header lead, so it sends the visitor to the format's own browse page; anything else is not there.
            var browse = rule.FrontPage
                ? services.GetRequiredService<FormatRegistry>().Find(repository.Format)?.RepositoryLinks(repository)
                    .FirstOrDefault(l => l.Id == "browse" && l.Controller != null && l.Action != null)
                : null;

            return browse == null
                ? new NotFoundResult()
                : new RedirectToActionResult(browse.Action!, browse.Controller, new { repo = repository.IsDefault ? null : repository.Name });
        }

        if (allowed && repository.Kind == RepositoryKind.Remote && rule.NotOnRemote)
        {
            return new NotFoundResult();
        }

        if (allowed && repository.Kind == RepositoryKind.Virtual)
        {
            if (rule.NotOnVirtual)
            {
                return new NotFoundResult();
            }

            // An upload to a virtual repository is an upload to the repository it deploys to: from here on the request
            // works in that one (staging, ownership, publishing), exactly as the push endpoint does.
            if (rule.Upload)
            {
                var target = await services.GetRequiredService<IVirtualRepositories>()
                    .DeployTargetAsync(repository, httpContext.RequestAborted);
                if (target == null)
                {
                    return new NotFoundResult();
                }

                httpContext.Items[RequestedVirtualItemKey] = repository;
                services.GetRequiredService<IRepositoryContext>().Use(target);
            }
        }

        if (!allowed)
        {
            // A visitor who is not signed in is sent to sign in (when the host has accounts) - they may be allowed
            // once they are; a signed-in user without Read gets 404, so nothing says the repository exists.
            var schemes = services.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();

            return httpContext.User.Identity?.IsAuthenticated != true
                && await schemes.GetDefaultChallengeSchemeAsync() != null
                ? new ChallengeResult()
                : new NotFoundResult();
        }

        return null;
    }
}
