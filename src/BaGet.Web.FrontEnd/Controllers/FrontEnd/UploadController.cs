using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Web.V3.Controllers.UI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Upload;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.V3.Controllers.FrontEnd;

/// <summary>
/// The NuGet upload page (<c>/upload</c>, <c>/r/{repo}/upload</c>): uploading through the browser in two steps, the first stages
/// the file and shows what it contains (<c>?handler=Stage</c>), the second publishes it (<c>?handler=Submit</c>) or discards it
/// (<c>?handler=Cancel</c>). It replaces the Razor Page <c>Upload</c>: same URLs and forms. The repository rule is
/// <see cref="RepositoryPageAttribute.Upload"/>: the caller needs the right to publish, and a virtual repository continues in its
/// deploy repository. View: <c>Views/Upload/Index.cshtml</c>. The work is <see cref="UploadUseCase"/>, reached through the JSON twin
/// <see cref="UploadApiController"/> in process; this controller only renders what it answers.
/// </summary>
/// <remarks>
/// Deliberately not an account controller: those answer 404 while accounts are disabled, and this page must still work there -
/// with a pasted API key, exactly as the push endpoint requires one. Every step is authenticated (session, or API key) by the
/// use case; the antiforgery token is validated by <see cref="UiControllerBase"/>.
/// </remarks>
[Route("upload")]
[RepositoryPage(Upload = true)]
public sealed class UploadController : UiControllerBase
{
    /// <summary>Step one: the form.</summary>
    [HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(string? id)
        => Render(await Api<UploadApiController, UploadViewModel>(api => api.Get(id)), "Index");

    /// <summary>Stages the chosen file and shows what it contains.</summary>
    [HttpPost, PageHandler("Stage")]
    [UploadSizeLimit]
    public async Task<IActionResult> Stage(string? id, IFormFile? package, string? apiKey, CancellationToken cancellationToken)
        => Render(await Api<UploadApiController, UploadViewModel>(api => api.Stage(id, package, apiKey, cancellationToken)), "Index");

    /// <summary>Publishes the staged upload.</summary>
    [HttpPost, PageHandler("Submit")]
    public async Task<IActionResult> Submit(string? id, string token, string? apiKey, bool acceptTerms, CancellationToken cancellationToken)
    {
        var request = new UploadTokenRequest { Token = token ?? string.Empty, ApiKey = apiKey, AcceptTerms = acceptTerms };

        return Render(await Api<UploadApiController, UploadViewModel>(api => api.Submit(id, request, cancellationToken)), "Index");
    }

    /// <summary>Discards the staged upload.</summary>
    [HttpPost, PageHandler("Cancel")]
    public async Task<IActionResult> Cancel(string? id, string token, string? apiKey, CancellationToken cancellationToken)
    {
        var request = new UploadTokenRequest { Token = token ?? string.Empty, ApiKey = apiKey };

        return Render(await Api<UploadApiController, UploadViewModel>(api => api.Cancel(id, request, cancellationToken)), "Index");
    }
}
