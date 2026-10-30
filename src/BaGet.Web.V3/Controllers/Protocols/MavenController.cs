using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using BaGet.Web.V3.Filters;

namespace BaGet.Web.V3.Controllers.Protocols;

/// <summary>
/// The Maven 2 repository protocol of a repository (<c>maven/…</c>): <c>GET</c> and <c>HEAD</c> of files, checksums and the
/// generated <c>maven-metadata.xml</c>, and <c>PUT</c> / <c>DELETE</c> of files. Every route is registered through
/// <see cref="BaGetEndpointBuilder.MapRoute"/> with the Maven format, so a request in a repository of another format is a 404.
/// Reads pass <see cref="RepositoryReadFilter"/> (Read on the repository and on <c>group:artifact</c>; an anonymous caller is
/// answered <c>401</c> with a Basic challenge, which makes Maven send the credentials of its <c>settings.xml</c> <c>&lt;server&gt;</c>);
/// writes are constrained to their methods and authorize themselves through <see cref="IMavenRepository"/> with the caller
/// <see cref="RequestPrincipalResolver.ResolveForWriteAsync"/> found (an API key as the Basic password; never the browser session).
/// Errors are plain text, which is what Maven prints.
/// </summary>
public class MavenController : ControllerBase, Microsoft.AspNetCore.Mvc.Filters.IActionFilter
{
    private readonly IMavenRepository _maven;
    private readonly RequestPrincipalResolver _principals;
    private readonly IRepositoryContext _repository;
    private readonly IOptionsSnapshot<BaGetOptions> _options;
    private readonly IOptionsSnapshot<MavenOptions> _mavenOptions;
    private readonly IAccessEvaluator _access;
    private readonly IScanGate? _scanGate;

    public MavenController(
        IMavenRepository maven,
        RequestPrincipalResolver principals,
        IRepositoryContext repository,
        IOptionsSnapshot<BaGetOptions> options,
        IOptionsSnapshot<MavenOptions> mavenOptions,
        IAccessEvaluator access,
        IScanGate? scanGate = null)
    {
        _scanGate = scanGate;
        _maven = maven ?? throw new ArgumentNullException(nameof(maven));
        _principals = principals ?? throw new ArgumentNullException(nameof(principals));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _mavenOptions = mavenOptions ?? throw new ArgumentNullException(nameof(mavenOptions));
        _access = access ?? throw new ArgumentNullException(nameof(access));
    }

    /// <summary>
    /// A remote repository whose upstream cannot answer (and has nothing stored to answer instead) is a bad gateway, not a missing
    /// file: Maven reports a 502 as a transfer failure it may retry elsewhere, where a 404 would be believed and remembered.
    /// </summary>
    [NonAction]
    public void OnActionExecuted(Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext context)
    {
        if (context.Exception is MavenUpstreamException e)
        {
            context.Result = Text(StatusCodes.Status502BadGateway, e.Message);
            context.ExceptionHandled = true;
        }
    }

    /// <inheritdoc/>
    [NonAction]
    public void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
    }

    private MavenRequestPath? RequestedPath
        => MavenNames.ParsePath(RouteData.Values.TryGetValue("path", out var path) ? path as string : null);

    private static IActionResult Text(int status, string message) => new ContentResult
    {
        StatusCode = status,
        ContentType = "text/plain; charset=utf-8",
        Content = message,
    };

    private IActionResult Challenge401(string message)
    {
        Response.Headers.WWWAuthenticate = BasicCredentials.Challenge;

        return Text(StatusCodes.Status401Unauthorized, message);
    }

    private IActionResult Respond(MavenResult result)
    {
        switch (result.Status)
        {
            case MavenStatus.Ok:
                return Text(StatusCodes.Status200OK, result.Message ?? "OK");
            case MavenStatus.Created:
                return Text(StatusCodes.Status201Created, result.Message ?? "Created");
            case MavenStatus.Unauthorized:
                return Challenge401(result.Message ?? "Authentication is required.");
            case MavenStatus.Forbidden:
                return Text(StatusCodes.Status403Forbidden, result.Message ?? "Forbidden.");
            case MavenStatus.NotFound:
                return Text(StatusCodes.Status404NotFound, result.Message ?? "Not found.");
            case MavenStatus.Conflict:
                return Text(StatusCodes.Status409Conflict, result.Message ?? "Conflict.");
            case MavenStatus.TooLarge:
                return Text(StatusCodes.Status413PayloadTooLarge, result.Message ?? "The file is too large.");
            default:
                return Text(StatusCodes.Status400BadRequest, result.Message ?? "The request is not valid.");
        }
    }

    /// <summary>
    /// <c>GET</c> / <c>HEAD</c> <c>maven/{path}</c>: a stored file, a checksum (<c>.sha1</c>, <c>.md5</c>, <c>.sha256</c>,
    /// <c>.sha512</c>), or the generated <c>maven-metadata.xml</c>. The file's checksums are also sent as <c>X-Checksum-*</c>
    /// headers, which Maven's resolver compares instead of fetching the checksum file.
    /// </summary>
    [AcceptVerbs("GET", "HEAD")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var path = RequestedPath;
        if (path == null) return Text(StatusCodes.Status404NotFound, "Not found.");

        // The scanning policy of the repository may refuse the files of a version (403 with the reason, by ScanBlockedFilter); the
        // generated maven-metadata.xml lists versions and stays readable.
        if (_scanGate != null && path.Kind == MavenPathKind.File && path.Candidates.Count > 0 && path.Candidates[0].Version != null)
        {
            await _scanGate.CheckDownloadAsync(path.Candidates[0].Name, path.Candidates[0].Version!, cancellationToken);
        }

        // A HEAD does not read the content: a remote repository answers it from the upstream's headers without fetching and keeping the file.
        var download = HttpMethods.IsHead(Request.Method)
            ? await _maven.HeadAsync(path, cancellationToken)
            : await _maven.OpenAsync(path, cancellationToken);
        if (download == null) return Text(StatusCodes.Status404NotFound, "Not found.");

        Response.RegisterForDispose(download);
        Response.Headers.LastModified = download.LastModified.ToString("R", CultureInfo.InvariantCulture);
        if (download.Sha1 != null)
        {
            Response.Headers.ETag = "\"" + download.Sha1 + "\"";
            Response.Headers["X-Checksum-Sha1"] = download.Sha1;
            Response.Headers["X-Checksum-Md5"] = download.Md5;
            Response.Headers["X-Checksum-Sha256"] = download.Sha256;
        }

        if (HttpMethods.IsHead(Request.Method))
        {
            Response.ContentType = download.ContentType;
            Response.ContentLength = download.Length;

            return new EmptyResult();
        }

        return new FileStreamResult(download.Content, download.ContentType) { EnableRangeProcessing = download.Content.CanSeek };
    }

    /// <summary>
    /// <c>PUT maven/{path}</c>: <c>mvn deploy</c> and Gradle's <c>maven-publish</c> upload each file, then its checksums and the
    /// metadata. 201 for what was stored or acknowledged, 409 for a release that exists (or a version the repository's policy
    /// refuses), 400 for a name that is not the layout's, 413 over <c>Maven:MaxFileBytes</c>.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Put(CancellationToken cancellationToken)
    {
        if (_options.Value.IsReadOnlyMode) return Text(StatusCodes.Status403Forbidden, "This server is read only.");

        // Answered before the body is read: Maven retries with credentials after the challenge.
        var caller = await _principals.ResolveForWriteAsync(HttpContext, cancellationToken);
        if (caller == null) return Challenge401("Authentication is required.");

        var path = RequestedPath;
        if (path == null) return Text(StatusCodes.Status400BadRequest, "The path is not a Maven repository path: group/as/folders/artifact/version/file.");

        // The files of a build are larger than the web server's default request limit; the service enforces Maven:MaxFileBytes.
        var feature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature != null && !feature.IsReadOnly) feature.MaxRequestBodySize = null;

        var limit = _mavenOptions.Value.MaxFileBytes;
        if (limit > 0 && Request.ContentLength > limit) return Text(StatusCodes.Status413PayloadTooLarge, $"The file is larger than {limit} bytes.");

        return Respond(await _maven.PutAsync(caller, path, Request.Body, cancellationToken));
    }

    /// <summary>
    /// <c>DELETE maven/{path}</c>: one file with its checksums. Not Maven's own protocol (Maven never deletes) but what
    /// Artifactory and Nexus accept, and what scripts use. A caller who may not read the artifact is told "not found" for
    /// a refusal and for a missing file alike. Metadata and checksum paths answer 204: they are generated.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken cancellationToken)
    {
        if (_options.Value.IsReadOnlyMode) return Text(StatusCodes.Status403Forbidden, "This server is read only.");

        var caller = await _principals.ResolveForWriteAsync(HttpContext, cancellationToken);
        if (caller == null) return Challenge401("Authentication is required.");

        var path = RequestedPath;
        if (path == null) return Text(StatusCodes.Status404NotFound, "Not found.");

        if (path.Kind == MavenPathKind.Metadata || path.ChecksumAlgorithm != null)
        {
            return Text(StatusCodes.Status204NoContent, string.Empty);
        }

        var coordinate = path.Candidates[0];
        var result = await _maven.DeleteFileAsync(caller, coordinate, path.BaseName, cancellationToken);
        if (result.Status is MavenStatus.Forbidden or MavenStatus.NotFound
            && !await _access.IsAllowedAsync(caller, _repository.Current, coordinate.Name, AccessAction.Read, cancellationToken))
        {
            return Text(StatusCodes.Status404NotFound, "Not found.");
        }

        return result.Succeeded ? Text(StatusCodes.Status204NoContent, string.Empty) : Respond(result);
    }
}
