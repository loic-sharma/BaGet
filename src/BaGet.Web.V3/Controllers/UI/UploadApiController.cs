using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using BaGet.Web.V3.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Upload;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.V3.Controllers.UI;

/// <summary>
/// The JSON twin of the NuGet upload screen (<c>/upload</c>, <c>/r/{repo}/upload</c>): <c>GET /api/ui/upload</c> is the form's state, and the three
/// steps are <c>POST /api/ui/upload/stage</c> (multipart: <c>package</c> and, while accounts are off, <c>apiKey</c>), <c>/submit</c> and <c>/cancel</c>
/// (JSON <see cref="UploadTokenRequest"/>). The repository rule is the page's (<see cref="RepositoryPageAttribute.Upload"/>: the caller needs the right to
/// publish, a virtual repository continues in its deploy repository). A refusal is <c>400</c>-class JSON carrying the status of the refusal (401, 403,
/// 404, 409, 500) and the form's state with the sentence in <c>errors</c>; an API key in the request authenticates the uploader within its scopes.
/// Not an account screen: it works with accounts off, with a pasted API key, like the push endpoint. <c>UploadController</c> calls it in process.
/// </summary>
[Route("api/ui/upload")]
[RepositoryPage(Upload = true)]
public sealed class UploadApiController : ApiControllerBase
{
    private readonly UploadUseCase _upload;

    /// <summary>Creates the controller.</summary>
    public UploadApiController(UploadUseCase upload)
    {
        _upload = upload ?? throw new ArgumentNullException(nameof(upload));
    }

    /// <summary>Step one: the form.</summary>
    [HttpGet, UiApi("Upload", "Index")]
    public Task<ActionResult<ScreenResult<UploadViewModel>>> Get(string? id)
        => Task.FromResult(Respond(_upload.Show(User, id)));

    /// <summary>Stages the chosen file and describes what it contains.</summary>
    [HttpPost("stage"), UiApi("Upload", "Index", Handler = "Stage")]
    [UploadSizeLimit]
    public async Task<ActionResult<ScreenResult<UploadViewModel>>> Stage(string? id, IFormFile? package, string? apiKey, CancellationToken cancellationToken)
        => Respond(await _upload.StageAsync(User, KeyCaller, id, package.ToUploadedFile(), apiKey, cancellationToken));

    /// <summary>Publishes the staged upload.</summary>
    [HttpPost("submit"), UiApi("Upload", "Index", Handler = "Submit")]
    public async Task<ActionResult<ScreenResult<UploadViewModel>>> Submit(string? id, [FromBody] UploadTokenRequest? request, CancellationToken cancellationToken)
    {
        request ??= new UploadTokenRequest();

        return Respond(await _upload.SubmitAsync(User, KeyCaller, id, request.Token, request.ApiKey, request.AcceptTerms, cancellationToken));
    }

    /// <summary>Discards the staged upload.</summary>
    [HttpPost("cancel"), UiApi("Upload", "Index", Handler = "Cancel")]
    public async Task<ActionResult<ScreenResult<UploadViewModel>>> Cancel(string? id, [FromBody] UploadTokenRequest? request, CancellationToken cancellationToken)
    {
        request ??= new UploadTokenRequest();

        return Respond(await _upload.CancelAsync(User, KeyCaller, id, request.Token, request.ApiKey, cancellationToken));
    }
}
