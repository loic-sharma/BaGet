using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using BaGet.Web.V3.Oci;

namespace BaGet.Web.V3.Controllers.Protocols;

/// <summary>
/// The OCI Distribution Spec v1.1 of a Docker repository, mounted at the server root (<c>/v2/…</c>, see
/// <see cref="BaGetEndpointBuilder.MapOciRoutes"/>): the version check, the <c>docker login</c> token service, blobs (monolithic,
/// chunked and mounted uploads; range reads), manifests and indexes by tag or digest, tag listing, referrers, and deletes.
/// <b>Every action other than <see cref="Base"/> and <see cref="Token"/> has been authorized</b> by
/// <see cref="OciAuthorizationFilter"/> before it runs (pull = Read, push = Deploy, delete = Delete on the image, through a token
/// or an API key); the repository of the request is already selected. Errors are the specification's JSON bodies.
/// </summary>
[ServiceFilter(typeof(OciAuthorizationFilter))]
public class OciController : ControllerBase
{
    private readonly IOciRegistry _registry;
    private readonly OciCallerResolver _callers;
    private readonly IOciTokenService _tokens;
    private readonly IOciAccess _access;
    private readonly IRepositoryService _repositories;
    private readonly IVirtualRepositories _virtuals;
    private readonly IUrlGenerator _urls;
    private readonly IOptionsSnapshot<OciOptions> _options;
    private readonly SystemTime _time;

    public OciController(
        IOciRegistry registry,
        OciCallerResolver callers,
        IOciTokenService tokens,
        IOciAccess access,
        IRepositoryService repositories,
        IVirtualRepositories virtuals,
        IUrlGenerator urls,
        IOptionsSnapshot<OciOptions> options,
        SystemTime time,
        IScanGate scanGate)
    {
        _scanGate = scanGate ?? throw new ArgumentNullException(nameof(scanGate));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _callers = callers ?? throw new ArgumentNullException(nameof(callers));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _virtuals = virtuals ?? throw new ArgumentNullException(nameof(virtuals));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    private readonly IScanGate _scanGate;

    private OciRequest Current => (OciRequest)HttpContext.Items[OciRequest.ItemKey]!;

    private string RepositoryPath => Current.Repository.Name;

    // The URL of a thing in the current image: .../v2/{repo}/{image}/{tail}
    private string ImageUrl(string tail) => $"{_urls.GetOciRegistryUrl()}{RepositoryPath}/{Current.Path.Image}/{tail}";

    private string Challenge(string? scope, string? error)
        => OciHttp.Challenge(_urls.GetOciRegistryUrl() + "token", _options.Value.Service, scope, error);

    // ---- /v2/ and /v2/token ------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>GET /v2/</c>: the version check. <c>200</c> for any caller the registry knows (a registry token, or an API key; an
    /// open server knows everyone); <c>401</c> with the token challenge for an anonymous one, which is what makes
    /// <c>docker login</c> ask for credentials. A public image can still be pulled anonymously: the client takes an anonymous
    /// token for it.
    /// </summary>
    [AcceptVerbs("GET", "HEAD")]
    public async Task<IActionResult> Base(CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext, cancellationToken);
        if (caller.IsAnonymous)
        {
            Response.Headers.WWWAuthenticate = new StringValues(Challenge(scope: null, caller.TokenRefused ? "invalid_token" : null));

            return OciHttp.Error(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "authentication required");
        }

        return new ContentResult { StatusCode = StatusCodes.Status200OK, ContentType = "application/json", Content = "{}" };
    }

    /// <summary>
    /// <c>GET /v2/token?service=&amp;scope=repository:{repo}/{image}:pull,push</c>: the <c>docker login</c> token service. The
    /// credentials are optional and are an API key as the Basic password (a user name with it); wrong credentials are <c>401</c>.
    /// The token grants, for each requested image of an existing Docker repository, the requested actions the caller holds, which
    /// may be none (the registry then refuses the next request with <c>insufficient_scope</c>). The token is signed, short-lived
    /// (<see cref="OciOptions.TokenLifetime"/>) and holds no credential. Only <c>GET</c> is served: clients fall back to it when
    /// the OAuth <c>POST</c> flow is not available.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Token(CancellationToken cancellationToken)
    {
        if (!_tokens.IsAvailable)
        {
            return OciHttp.Error(StatusCodes.Status503ServiceUnavailable, "UNSUPPORTED", "This server has no data protection, so it cannot issue registry tokens.");
        }

        var service = Request.Query["service"].ToString();
        if (!string.IsNullOrEmpty(service) && !string.Equals(service, _options.Value.Service, StringComparison.Ordinal))
        {
            return OciHttp.Error(StatusCodes.Status400BadRequest, "UNSUPPORTED", "The service is not this registry.");
        }

        var caller = await _callers.ResolveAsync(HttpContext, cancellationToken);
        var presented = BasicCredentials.Read(Request) != null || BasicCredentials.ReadBearer(Request) != null;
        if (presented && caller.Principal == null && caller.Token == null)
        {
            Response.Headers.WWWAuthenticate = BasicCredentials.Challenge;

            return OciHttp.Error(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "incorrect username or password");
        }

        // A registry token cannot be traded for another one: the token endpoint is for credentials.
        var principal = caller.Principal;

        var grants = new System.Collections.Generic.List<OciGrant>();
        foreach (var scope in Request.Query["scope"].SelectMany(s => (s ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            var requested = OciScopes.Parse(scope);
            if (requested == null || !OciNames.IsValidImage(requested.Image)) continue;

            var repository = await _repositories.FindAsync(requested.Repository, cancellationToken);
            if (repository is not { Format: RepositoryFormat.Docker }) continue;

            var granted = await GrantedAsync(principal, repository, requested.Image, requested.Actions, cancellationToken);
            if (granted == OciActions.None) continue;

            Grant(grants, requested.Repository, requested.Image, granted);

            // A virtual repository is pulled through its members, each judged on its own: the token names the ones the caller may pull from.
            if (repository.Kind == RepositoryKind.Virtual && granted.HasFlag(OciActions.Pull))
            {
                foreach (var member in await _virtuals.EffectiveMembersAsync(repository, cancellationToken))
                {
                    if (await _access.IsAllowedAsync(principal, member, requested.Image, OciActions.Pull, cancellationToken)) Grant(grants, member.Name, requested.Image, OciActions.Pull);
                }
            }
        }

        var token = _tokens.Issue(principal?.UserName, grants);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("token", token.Token);
            writer.WriteString("access_token", token.Token);
            writer.WriteNumber("expires_in", token.ExpiresIn);
            writer.WriteString("issued_at", token.IssuedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        Response.Headers.CacheControl = "no-store";

        return new ContentResult { StatusCode = StatusCodes.Status200OK, ContentType = "application/json", Content = Encoding.UTF8.GetString(stream.ToArray()) };
    }

    private static void Grant(System.Collections.Generic.List<OciGrant> grants, string repository, string image, OciActions actions)
    {
        var existing = grants.FindIndex(g => g.Repository == repository && g.Image == image);
        if (existing >= 0) grants[existing] = grants[existing] with { Actions = grants[existing].Actions | actions };
        else grants.Add(new OciGrant(repository, image, actions));
    }

    // What the principal holds of the requested actions on the image: in a local repository, as is; in a remote one only pulls (it takes no
    // changes); in a virtual one pulls on the virtual repository itself and pushes and deletes where they land, in its deploy repository.
    private async Task<OciActions> GrantedAsync(BaGetPrincipal? principal, RepositoryInfo repository, string image, OciActions requested, CancellationToken cancellationToken)
    {
        switch (repository.Kind)
        {
            case RepositoryKind.Local:
                return await _access.GrantedAsync(principal, repository, image, requested, cancellationToken);

            case RepositoryKind.Remote:
                return await _access.GrantedAsync(principal, repository, image, requested & OciActions.Pull, cancellationToken);

            default:
                var granted = await _access.GrantedAsync(principal, repository, image, requested & OciActions.Pull, cancellationToken);
                var writes = requested & (OciActions.Push | OciActions.Delete);
                if (writes != OciActions.None && await _virtuals.DeployTargetAsync(repository, cancellationToken) is { } target)
                {
                    granted |= await _access.GrantedAsync(principal, target, image, writes, cancellationToken);
                }

                return granted;
        }
    }

    // ---- reads ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>GET</c>/<c>HEAD</c> of a blob (with <c>Range</c>), a manifest (by tag or digest), a tag list, the referrers of a digest,
    /// or the state of an upload session.
    /// </summary>
    [AcceptVerbs("GET", "HEAD")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        try
        {
            return Current.Path.Kind switch
            {
                OciPathKind.Blob => await GetBlobAsync(cancellationToken),
                OciPathKind.Manifest => await GetManifestAsync(cancellationToken),
                OciPathKind.TagList => await ListTagsAsync(cancellationToken),
                OciPathKind.Referrers => await GetReferrersAsync(cancellationToken),
                OciPathKind.Upload => await GetUploadAsync(cancellationToken),
                _ => OciHttp.Error(StatusCodes.Status405MethodNotAllowed, "UNSUPPORTED", "The method is not supported for this endpoint."),
            };
        }
        catch (OciException e)
        {
            return OciHttp.Error(e);
        }
    }

    private async Task<IActionResult> GetBlobAsync(CancellationToken cancellationToken)
    {
        var path = Current.Path;
        if (!OciNames.IsValidDigest(path.Reference)) return OciHttp.Error(StatusCodes.Status404NotFound, "BLOB_UNKNOWN", "blob unknown to registry", path.Reference);

        var info = await _registry.GetBlobInfoAsync(path.Image, path.Reference!, cancellationToken);
        if (info == null) return OciHttp.Error(StatusCodes.Status404NotFound, "BLOB_UNKNOWN", "blob unknown to registry", path.Reference);

        Response.Headers[OciHttp.DigestHeader] = info.Digest;
        Response.Headers.AcceptRanges = "bytes";
        Response.Headers.ETag = $"\"{info.Digest}\"";

        long offset = 0;
        long? length = null;
        var partial = false;
        if (Request.Headers.TryGetValue(Microsoft.Net.Http.Headers.HeaderNames.Range, out var rangeHeader) && TryParseRange(rangeHeader.ToString(), info.Size, out var start, out var end, out var satisfiable))
        {
            if (!satisfiable)
            {
                Response.Headers.ContentRange = $"bytes */{info.Size.ToString(CultureInfo.InvariantCulture)}";

                return new StatusCodeResult(StatusCodes.Status416RangeNotSatisfiable);
            }

            offset = start;
            length = end - start + 1;
            partial = true;
        }

        if (HttpMethods.IsHead(Request.Method))
        {
            Response.StatusCode = partial ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
            Response.ContentType = "application/octet-stream";
            Response.ContentLength = length ?? info.Size;
            if (partial) Response.Headers.ContentRange = $"bytes {offset}-{offset + length!.Value - 1}/{info.Size}";

            return new EmptyResult();
        }

        var blob = await _registry.OpenBlobAsync(path.Image, path.Reference!, offset, length, cancellationToken);
        if (blob == null) return OciHttp.Error(StatusCodes.Status404NotFound, "BLOB_UNKNOWN", "blob unknown to registry", path.Reference);

        Response.RegisterForDispose(blob);
        Response.StatusCode = partial ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
        Response.ContentType = "application/octet-stream";
        Response.ContentLength = blob.Length;
        if (partial) Response.Headers.ContentRange = $"bytes {blob.Offset}-{blob.Offset + blob.Length - 1}/{blob.TotalSize}";

        await blob.Content.CopyToAsync(Response.Body, cancellationToken);

        return new EmptyResult();
    }

    // A single "bytes=a-b", "bytes=a-" or "bytes=-n" range. Returns false for anything else (the whole blob is then served,
    // as the HTTP specification allows); satisfiable false means a well-formed range past the end (416).
    private static bool TryParseRange(string header, long size, out long start, out long end, out bool satisfiable)
    {
        start = 0;
        end = 0;
        satisfiable = false;

        const string unit = "bytes=";
        if (!header.StartsWith(unit, StringComparison.OrdinalIgnoreCase) || header.Contains(',')) return false;

        var spec = header.Substring(unit.Length).Trim();
        var dash = spec.IndexOf('-');
        if (dash < 0) return false;

        var first = spec.Substring(0, dash);
        var last = spec.Substring(dash + 1);
        if (first.Length == 0)
        {
            if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix == 0) return false;

            suffix = Math.Min(suffix, size);
            start = size - suffix;
            end = size - 1;
        }
        else
        {
            if (!long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out start)) return false;

            if (last.Length == 0) end = size - 1;
            else if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out end)) return false;

            if (end < start) return false;
            end = Math.Min(end, size - 1);
        }

        satisfiable = size > 0 && start < size;

        return true;
    }

    private async Task<IActionResult> GetManifestAsync(CancellationToken cancellationToken)
    {
        var path = Current.Path;
        var manifest = await _registry.GetManifestAsync(path.Image, path.Reference!, cancellationToken);
        if (manifest == null) return OciHttp.Error(StatusCodes.Status404NotFound, "MANIFEST_UNKNOWN", "manifest unknown", path.Reference);

        // The scanning policy of the repository may refuse an image with known vulnerabilities (403 DENIED, by ScanBlockedFilter).
        // An index only lists platform manifests; each of those is checked when the client asks for it.
        // The scanner's own pull (Trivy) is exempt: refusing it would keep the image from ever being scanned or rescanned
        // (without a configured scanner user, only while the image has no successful scan).
        if (!OciNames.IsIndex(manifest.MediaType) && !await _scanGate.IsScannerPullAsync(path.Image, manifest.Digest, Current.Caller.Name, cancellationToken))
        {
            await _scanGate.CheckDownloadAsync(path.Image, manifest.Digest, cancellationToken);
        }

        Response.Headers[OciHttp.DigestHeader] = manifest.Digest;
        Response.Headers.ETag = $"\"{manifest.Digest}\"";
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = manifest.MediaType;
        Response.ContentLength = manifest.Content.Length;

        if (!HttpMethods.IsHead(Request.Method)) await Response.Body.WriteAsync(manifest.Content, cancellationToken);

        return new EmptyResult();
    }

    private async Task<IActionResult> ListTagsAsync(CancellationToken cancellationToken)
    {
        var path = Current.Path;
        int? count = null;
        if (Request.Query.TryGetValue("n", out var n) && n.Count > 0)
        {
            if (!int.TryParse(n[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) return OciHttp.Error(StatusCodes.Status400BadRequest, "PAGINATION_NUMBER_INVALID", "n must be a non-negative integer");

            count = parsed;
        }

        var last = Request.Query.TryGetValue("last", out var lastValue) ? lastValue.ToString() : null;
        var list = await _registry.ListTagsAsync(path.Image, count, last, cancellationToken);
        if (list == null) return OciHttp.Error(StatusCodes.Status404NotFound, "NAME_UNKNOWN", "repository name not known to registry", path.Image);

        if (list.HasMore && list.Tags.Count > 0)
        {
            var next = $"{ImageUrl("tags/list")}?n={count!.Value.ToString(CultureInfo.InvariantCulture)}&last={Uri.EscapeDataString(list.Tags[^1])}";
            Response.Headers.Link = $"<{next}>; rel=\"next\"";
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("name", $"{RepositoryPath}/{path.Image}");
            writer.WriteStartArray("tags");
            foreach (var tag in list.Tags) writer.WriteStringValue(tag);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return new ContentResult { StatusCode = StatusCodes.Status200OK, ContentType = "application/json", Content = Encoding.UTF8.GetString(stream.ToArray()) };
    }

    private async Task<IActionResult> GetReferrersAsync(CancellationToken cancellationToken)
    {
        var path = Current.Path;
        if (!OciNames.IsValidDigest(path.Reference)) return OciHttp.Error(StatusCodes.Status400BadRequest, "DIGEST_INVALID", "the digest is not valid", path.Reference);

        var artifactType = Request.Query.TryGetValue("artifactType", out var filter) ? filter.ToString() : null;
        var referrers = await _registry.GetReferrersAsync(path.Image, path.Reference!, artifactType, cancellationToken);
        if (referrers == null) return OciHttp.Error(StatusCodes.Status404NotFound, "NAME_UNKNOWN", "repository name not known to registry", path.Image);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 2);
            writer.WriteString("mediaType", OciNames.OciIndexType);
            writer.WriteStartArray("manifests");
            foreach (var referrer in referrers)
            {
                writer.WriteStartObject();
                writer.WriteString("mediaType", referrer.MediaType);
                writer.WriteString("digest", referrer.Digest);
                writer.WriteNumber("size", referrer.Size);
                if (referrer.ArtifactType != null) writer.WriteString("artifactType", referrer.ArtifactType);
                if (referrer.Annotations is { Count: > 0 })
                {
                    writer.WriteStartObject("annotations");
                    foreach (var annotation in referrer.Annotations) writer.WriteString(annotation.Key, annotation.Value);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        if (!string.IsNullOrEmpty(artifactType)) Response.Headers["OCI-Filters-Applied"] = "artifactType";

        return new ContentResult { StatusCode = StatusCodes.Status200OK, ContentType = OciNames.OciIndexType, Content = Encoding.UTF8.GetString(stream.ToArray()) };
    }

    private async Task<IActionResult> GetUploadAsync(CancellationToken cancellationToken)
    {
        var path = Current.Path;
        var state = await _registry.GetUploadAsync(path.Image, path.Reference!, cancellationToken);
        if (state == null) return OciHttp.Error(StatusCodes.Status404NotFound, "BLOB_UPLOAD_UNKNOWN", "blob upload unknown to registry", path.Reference);

        SetUploadHeaders(state);

        return new StatusCodeResult(StatusCodes.Status204NoContent);
    }

    private void SetUploadHeaders(OciUploadState state)
    {
        Response.Headers.Location = ImageUrl("blobs/uploads/" + state.Reference);
        Response.Headers[OciHttp.UploadUuidHeader] = state.Reference;
        Response.Headers["Range"] = "0-" + Math.Max(state.Length - 1, 0).ToString(CultureInfo.InvariantCulture);
    }

    // ---- uploads ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>POST blobs/uploads/</c>: starts an upload session (<c>202</c>), or stores a whole blob sent with
    /// <c>?digest=</c> (<c>201</c>), or mounts a blob of another image of the repository (<c>?mount=&amp;from=</c>, <c>201</c>; a
    /// blob that cannot be mounted starts a normal session, as the specification allows).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        var path = Current.Path;
        try
        {
            LiftBodyLimit();

            if (Request.Query.TryGetValue("mount", out var mount) && Request.Query.TryGetValue("from", out var from) && await TryMountAsync(mount.ToString(), from.ToString(), cancellationToken))
            {
                return Created(mount.ToString());
            }

            if (Request.Query.TryGetValue("digest", out var digest))
            {
                var stored = await _registry.PutBlobAsync(path.Image, digest.ToString(), Request.Body, cancellationToken);

                return Created(stored.Digest);
            }

            var state = await _registry.StartUploadAsync(path.Image, cancellationToken);
            SetUploadHeaders(state);
            Response.Headers.ContentLength = 0;

            return new StatusCodeResult(StatusCodes.Status202Accepted);
        }
        catch (OciException e)
        {
            return OciHttp.Error(e);
        }
    }

    private async Task<bool> TryMountAsync(string digest, string from, CancellationToken cancellationToken)
    {
        // "from" is the full name: the BaGet repository, then the image. Only another image of this repository can be mounted
        // (a blob is stored per repository), and only by a caller who may pull it.
        var slash = from.IndexOf('/');
        if (slash <= 0 || !string.Equals(from.Substring(0, slash), RepositoryPath, StringComparison.OrdinalIgnoreCase)) return false;

        var source = from.Substring(slash + 1);
        if (!OciNames.IsValidImage(source) || !OciNames.IsValidDigest(digest)) return false;
        // The link is copied inside Target (the deploy repository of a virtual one), so the right to pull the source is the one
        // in Target: a token must carry a grant for that repository, not only for the virtual name the client used.
        if (!await _callers.AllowsAsync(Current.Caller, Current.Target, source, OciActions.Pull, cancellationToken)) return false;

        return await _registry.MountBlobAsync(Current.Path.Image, digest, source, cancellationToken);
    }

    private IActionResult Created(string digest)
    {
        Response.Headers.Location = ImageUrl("blobs/" + digest);
        Response.Headers[OciHttp.DigestHeader] = digest;
        Response.Headers.ContentLength = 0;

        return new StatusCodeResult(StatusCodes.Status201Created);
    }

    private void LiftBodyLimit()
    {
        // A layer is far larger than a form: the registry bounds nothing here; the store streams it.
        var feature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature != null && !feature.IsReadOnly) feature.MaxRequestBodySize = null;
    }

    // "Content-Range: 0-1023" (or "bytes 0-1023") of a chunk: where it starts.
    private long? ChunkStart()
    {
        if (!Request.Headers.TryGetValue("Content-Range", out var header)) return null;

        var text = header.ToString().Trim();
        if (text.StartsWith("bytes", StringComparison.OrdinalIgnoreCase)) text = text.Substring(5).TrimStart(' ', '=');
        var dash = text.IndexOf('-');
        if (dash <= 0) return null;

        return long.TryParse(text.Substring(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : null;
    }

    /// <summary><c>PATCH blobs/uploads/{ref}</c>: appends a chunk (<c>202</c>); a chunk that does not start where the upload ends is <c>416</c>.</summary>
    [HttpPatch]
    public async Task<IActionResult> Patch(CancellationToken cancellationToken)
    {
        var path = Current.Path;
        try
        {
            LiftBodyLimit();

            var state = await _registry.AppendAsync(path.Image, path.Reference!, ChunkStart(), Request.Body, cancellationToken);
            SetUploadHeaders(state);
            Response.Headers.ContentLength = 0;

            return new StatusCodeResult(StatusCodes.Status202Accepted);
        }
        catch (OciException e)
        {
            if (e.Code == OciErrorCode.RangeInvalid && e.UploadLength != null)
            {
                SetUploadHeaders(new OciUploadState(path.Reference!, e.UploadLength.Value));
            }

            return OciHttp.Error(e);
        }
    }

    /// <summary>
    /// <c>PUT blobs/uploads/{ref}?digest=</c>: the last chunk (the body may be empty), the digest check, and the blob is stored
    /// (<c>201</c>). <c>PUT manifests/{ref}</c>: stores a manifest or index under a tag or by digest (<c>201</c>).
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Put(CancellationToken cancellationToken)
    {
        var path = Current.Path;
        try
        {
            if (path.Kind == OciPathKind.Manifest) return await PutManifestAsync(cancellationToken);

            LiftBodyLimit();

            if (!Request.Query.TryGetValue("digest", out var digest) || string.IsNullOrEmpty(digest))
            {
                return OciHttp.Error(StatusCodes.Status400BadRequest, "DIGEST_INVALID", "The digest query parameter is required.");
            }

            var hasBody = Request.ContentLength != 0;
            var stored = await _registry.CompleteUploadAsync(path.Image, path.Reference!, digest.ToString(), ChunkStart(), hasBody ? Request.Body : null, cancellationToken);

            return Created(stored.Digest);
        }
        catch (OciException e)
        {
            return OciHttp.Error(e);
        }
    }

    private async Task<IActionResult> PutManifestAsync(CancellationToken cancellationToken)
    {
        var path = Current.Path;
        var limit = _options.Value.MaxManifestBytes;

        // The body is read up to the limit plus one byte: a longer one is refused without being held.
        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int read;
            while ((read = await Request.Body.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
            {
                if (buffer.Length + read > limit) return OciHttp.Error(StatusCodes.Status413PayloadTooLarge, "MANIFEST_INVALID", $"The manifest is larger than {limit} bytes.");

                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        var mediaType = Request.ContentType?.Split(';')[0].Trim();
        var put = await _registry.PutManifestAsync(path.Image, path.Reference!, mediaType, buffer.ToArray(), cancellationToken);

        // An image cannot be inspected before it is stored: with a deploy threshold in force it is scanned now and removed again when
        // it reaches it (403 DENIED); otherwise a scan is queued. Indexes and artifacts whose config is not an image config are not images; `subject` and `artifactType` are ignored.
        if (DockerFormatScanner.IsImage(mediaType, Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length)))
        {
            try
            {
                await _scanGate.CheckStoredAsync(path.Image, put.Digest, cancellationToken);
            }
            catch (ScanBlockedException)
            {
                await OciPushUndo.UndoAsync(_registry, path.Image, path.Reference!, put);

                throw;
            }
        }

        Response.Headers.Location = ImageUrl("manifests/" + put.Digest);
        Response.Headers[OciHttp.DigestHeader] = put.Digest;
        if (put.SubjectDigest != null) Response.Headers["OCI-Subject"] = put.SubjectDigest;
        Response.Headers.ContentLength = 0;

        return new StatusCodeResult(StatusCodes.Status201Created);
    }

    // ---- deletes ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>DELETE</c> of a manifest by digest (with its tags) or of a tag (<c>manifests/{tag}</c>: the tag only), of a blob from the
    /// image, or of an upload session; <c>202</c> (<c>204</c> for a session).
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken cancellationToken)
    {
        var path = Current.Path;
        try
        {
            switch (path.Kind)
            {
                case OciPathKind.Manifest when OciNames.LooksLikeDigest(path.Reference):
                    return await _registry.DeleteManifestAsync(path.Image, path.Reference!, cancellationToken)
                        ? AcceptedEmpty()
                        : OciHttp.Error(StatusCodes.Status404NotFound, "MANIFEST_UNKNOWN", "manifest unknown", path.Reference);

                case OciPathKind.Manifest:
                    return await _registry.DeleteTagAsync(path.Image, path.Reference!, cancellationToken)
                        ? AcceptedEmpty()
                        : OciHttp.Error(StatusCodes.Status404NotFound, "MANIFEST_UNKNOWN", "manifest unknown", path.Reference);

                case OciPathKind.Blob:
                    return await _registry.DeleteBlobAsync(path.Image, path.Reference!, cancellationToken)
                        ? AcceptedEmpty()
                        : OciHttp.Error(StatusCodes.Status404NotFound, "BLOB_UNKNOWN", "blob unknown to registry", path.Reference);

                case OciPathKind.Upload:
                    return await _registry.CancelUploadAsync(path.Image, path.Reference!, cancellationToken)
                        ? new StatusCodeResult(StatusCodes.Status204NoContent)
                        : OciHttp.Error(StatusCodes.Status404NotFound, "BLOB_UPLOAD_UNKNOWN", "blob upload unknown to registry", path.Reference);

                default:
                    return OciHttp.Error(StatusCodes.Status405MethodNotAllowed, "UNSUPPORTED", "The method is not supported for this endpoint.");
            }
        }
        catch (OciException e)
        {
            return OciHttp.Error(e);
        }
    }

    private IActionResult AcceptedEmpty()
    {
        Response.Headers.ContentLength = 0;

        return new StatusCodeResult(StatusCodes.Status202Accepted);
    }
}
