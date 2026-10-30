using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Web.V3.Controllers.UI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Npm;

namespace BaGet.Web.V3.Controllers.FrontEnd;

/// <summary>
/// The web pages of an npm repository: the package list (<c>/npm-packages</c>), one package (<c>/npm-packages/{name}</c>) and the
/// browser upload (<c>/npm-upload</c>). They replace the Razor Pages <c>NpmPackages</c>, <c>NpmPackage</c> and <c>NpmUpload</c>: same URLs, same repository rules (<see cref="RepositoryPageAttribute"/>: Read on the repository, on
/// the package for the package page, the right to publish for the upload; 404 in a repository that is not npm's). Views are in
/// <c>Views/NpmBrowse/</c>. The work is <see cref="BaGet.Web.Application.UseCases.NpmBrowseUseCase"/>, reached through the JSON twin
/// <see cref="NpmBrowseApiController"/> in process; this controller only renders what it answers.
/// </summary>
public sealed class NpmBrowseController : UiControllerBase
{
    /// <summary>The package list with a search box.</summary>
    [Route("npm-packages"), HttpGet, HttpHead, PageHandler]
    [RepositoryPage(Format = RepositoryFormat.Npm)]
    public async Task<IActionResult> Packages(
        string? q,
        [Range(1, int.MaxValue)] int p = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest();

        return Render(await Api<NpmBrowseApiController, NpmPackagesViewModel>(api => api.Packages(q, p, cancellationToken)), "Packages");
    }

    /// <summary>One package: readme, versions, dist-tags, install commands.</summary>
    [Route("npm-packages/{*id}"), HttpGet, HttpHead, PageHandler]
    [RepositoryPage(Format = RepositoryFormat.Npm)]
    public async Task<IActionResult> Package(string? id, string? version, CancellationToken cancellationToken = default)
        => Render(await Api<NpmBrowseApiController, NpmPackageViewModel>(api => api.Package(id, version, cancellationToken)), "Package");

    /// <summary>The upload form.</summary>
    [Route("npm-upload"), HttpGet, HttpHead, PageHandler]
    [RepositoryPage(Format = RepositoryFormat.Npm, Upload = true)]
    public async Task<IActionResult> Upload()
        => Render(await Api<NpmBrowseApiController, NpmUploadViewModel>(api => api.Upload()), "Upload");

    /// <summary>Publishes the chosen tarball with the dist-tag <paramref name="tag"/> (default <c>latest</c>).</summary>
    [Route("npm-upload"), HttpPost, PageHandler]
    [RepositoryPage(Format = RepositoryFormat.Npm, Upload = true)]
    [UploadSizeLimit]
    public async Task<IActionResult> UploadPost(IFormFile? package, string? tag, string? apiKey, CancellationToken cancellationToken)
        => Render(await Api<NpmBrowseApiController, NpmUploadViewModel>(api => api.UploadPost(package, tag, apiKey, cancellationToken)), "Upload");
}
