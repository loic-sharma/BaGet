using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.Models.Api;
using BaGet.Web.V3.Api;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Controllers.Api;

/// <summary>
/// Repositories: list, create (local, remote, virtual; any format), switch anonymous read, delete, and the kind-specific parts
/// (a remote repository's upstream, cache and purge; a virtual repository's members and resolution preview). The services are the
/// ones the Repositories, RepositoryEdit and RepositoryDelete pages call, and the rules are theirs: a remote repository's create,
/// save, test and purge ask for the owner's password as those pages do; deleting asks for nothing, as RepositoryDelete does.
/// </summary>
[Route(ApiCatalog.RootTemplate + "/repositories")]
public class RepositoriesController : AdminApiController
{
    private readonly IRepositoryService _repositories;
    private readonly IRepositoryAdministration _administration;
    private readonly FormatRegistry _formats;

    /// <summary>Creates the controller.</summary>
    public RepositoriesController(IRepositoryService repositories, IRepositoryAdministration administration, FormatRegistry formats)
    {
        _repositories = repositories;
        _administration = administration;
        _formats = formats;
    }

    /// <summary>Lists repositories, optionally by format, kind or a name fragment.</summary>
    [HttpGet]
    [ApiOperation("List repositories (filter by format, kind or name)", "AdminRepositories", "Index", Response = typeof(RepositoryDto), PagedList = true)]
    public async Task<IActionResult> List(string? format, string? kind, string? q, int? page, int? pageSize)
    {
        var all = await _administration.ListAsync(Cancellation);
        var filtered = RepositorySummaryFilter.Apply(all, format, kind, q)
            .Select(Dto)
            .ToList();

        return RespondPage(filtered, page, pageSize);
    }

    /// <summary>The formats and what each asks for.</summary>
    [HttpGet("/api/v1/formats")]
    [ApiOperation("List the package formats and the kinds and fields each offers", "AdminOperations", "Formats", Response = typeof(FormatDto))]
    public IActionResult Formats()
        => Respond(_formats.Modules.Select(m => new FormatDto(
            m.Format.ToString(),
            m.DisplayName,
            m.CreatableKinds.Select(k => k.ToString()).ToList(),
            m.SupportsVirtual,
            m.FeedPath,
            m.CreateFields.Select(f => new FormatFieldDto(f.Name, f.Label, f.Help, f.Required, f.Options)).ToList())).ToList());

    /// <summary>One repository with its kind's details.</summary>
    [HttpGet("{name}")]
    [ApiOperation("Show a repository with its upstream, cache or members", "AdminRepositories", "Edit", Response = typeof(RepositoryDetailDto))]
    public async Task<IActionResult> Get(string name)
    {
        var summary = await _administration.FindAsync(name, Cancellation);
        if (summary == null) return ApiProblems.NotFound($"repository '{name}'");

        RemoteSettingsDto? remote = null;
        RemoteCacheOverview? cache = null;
        VirtualDto? virtualDto = null;
        var virtuals = Service<IVirtualRepositoryAdministration>();
        if (summary.Kind == RepositoryKind.Remote)
        {
            var remotes = Service<IRemoteRepositoryAdministration>();
            var detail = await remotes.FindAsync(name, Cancellation);
            if (detail != null)
            {
                remote = ToDto(detail);
                cache = await remotes.GetCacheAsync(name, Cancellation);
            }
        }
        else if (summary.Kind == RepositoryKind.Virtual)
        {
            var detail = await virtuals.FindAsync(name, Cancellation);
            if (detail != null)
            {
                virtualDto = new VirtualDto(
                    detail.Members.Select(m => new VirtualMemberDto(m.Name, m.Exists)).ToList(),
                    detail.DeployRepository,
                    detail.DeployTarget?.Name,
                    detail.EffectiveMembers.Select(m => m.Name).ToList());
            }
        }

        string? project = null;
        var info = await _repositories.FindAsync(name, Cancellation);
        if (info != null) project = (await Service<IAccessEvaluator>().DescribeAsync(info, Cancellation)).Project;

        var referencedBy = summary.Kind == RepositoryKind.Virtual ? Array.Empty<string>() : await virtuals.ReferencedByAsync(name, Cancellation);

        return Respond(new RepositoryDetailDto(Dto(summary), project, remote, cache, virtualDto, referencedBy));
    }

    /// <summary>Who may do what in the repository (the effective-access panel of RepositoryEdit).</summary>
    [HttpGet("{name}/access")]
    [ApiOperation("Show who holds which action in a repository, and why", "AdminRepositories", "Edit", Response = typeof(RepositoryAccessSummary))]
    public async Task<IActionResult> Access(string name)
    {
        var info = await _repositories.FindAsync(name, Cancellation);
        if (info == null) return ApiProblems.NotFound($"repository '{name}'");

        return Respond(await Service<IAccessEvaluator>().DescribeAsync(info, Cancellation));
    }

    /// <summary>Creates a repository.</summary>
    [HttpPost]
    [ApiOperation("Create a local, remote or virtual repository of any format (remote asks for the password)", "AdminRepositories", "Index", Confirm = true, Response = typeof(RepositoryDto), Status = 201)]
    public async Task<IActionResult> Create([FromBody] CreateRepositoryRequest request)
    {
        ThrowIfClosed();

        var name = request.Name?.Trim();
        if (!RepositoryNames.IsValid(name, out var invalid)) return ApiProblems.BadRequest(invalid ?? "The name is not valid.");

        if (!TryParse<RepositoryFormat>(request.Format, RepositoryFormat.NuGet, out var format)) return ApiProblems.BadRequest($"Unknown format '{request.Format}'.");
        if (!TryParse<RepositoryKind>(request.Kind, RepositoryKind.Local, out var kind)) return ApiProblems.BadRequest($"Unknown kind '{request.Kind}'.");

        var module = _formats.Find(format);
        if (module == null) return ApiProblems.BadRequest($"No module serves the format {format}.");

        switch (kind)
        {
            case RepositoryKind.Remote:
            {
                if (!module.CreatableKinds.Contains(RepositoryKind.Remote)) return ApiProblems.BadRequest($"{module.DisplayName} has no remote repositories.");
                if (request.Remote == null) return ApiProblems.BadRequest("A remote repository needs 'remote' (its upstream).");
                var refusal = await RequireConfirmationAsync();
                if (refusal != null) return refusal;

                var settings = request.Remote.ToSettings();
                var anonymous = request.AnonymousRead ?? !settings.HasCredentials;
                await _repositories.CreateAsync(name!, format, RepositoryKind.Remote, settings, anonymous, Cancellation);
                break;
            }

            case RepositoryKind.Virtual:
                if (!module.SupportsVirtual) return ApiProblems.BadRequest($"{module.DisplayName} has no virtual repositories.");
                await Service<IVirtualRepositoryAdministration>().CreateAsync(
                    name!, request.Virtual?.ToSettings() ?? new VirtualRepositorySettings(), Cancellation, Actor, request.AnonymousRead ?? true, format);
                break;

            default:
                if (format == RepositoryFormat.NuGet)
                {
                    await _repositories.CreateAsync(name!, RepositoryFormat.NuGet, RepositoryKind.Local, Cancellation);
                    if (request.AnonymousRead == false) await _administration.SetAnonymousReadAsync(name!, false, Cancellation, Actor);
                }
                else
                {
                    if (!module.CreatableKinds.Contains(RepositoryKind.Local)) return ApiProblems.BadRequest($"{module.DisplayName} has no local repositories.");
                    await _repositories.CreateAsync(name!, format, RepositoryKind.Local, request.Fields ?? new Dictionary<string, string?>(), request.AnonymousRead ?? true, Cancellation);
                }

                break;
        }

        var created = await _administration.FindAsync(name, Cancellation);

        return Respond(Dto(created!), 201, $"{ApiCatalog.Root}/repositories/{Uri.EscapeDataString(name!)}");
    }

    /// <summary>Changes a repository's anonymous-read switch.</summary>
    [HttpPatch("{name}")]
    [ApiOperation("Switch a repository's anonymous read (a remote repository asks for the password)", "AdminRepositories", "Edit", Confirm = true, Response = typeof(RepositoryDto))]
    public async Task<IActionResult> Patch(string name, [FromBody] PatchRepositoryRequest request)
    {
        ThrowIfClosed();
        var summary = await _administration.FindAsync(name, Cancellation);
        if (summary == null) return ApiProblems.NotFound($"repository '{name}'");
        if (request.AnonymousRead == null) return ApiProblems.BadRequest("Nothing to change: send 'anonymousRead'.");

        if (summary.Kind == RepositoryKind.Remote)
        {
            var refusal = await RequireConfirmationAsync();
            if (refusal != null) return refusal;
        }

        await _administration.SetAnonymousReadAsync(name, request.AnonymousRead.Value, Cancellation, Actor);

        return Respond(Dto((await _administration.FindAsync(name, Cancellation))!));
    }

    /// <summary>Deletes an empty repository.</summary>
    [HttpDelete("{name}")]
    [ApiOperation("Delete an empty repository (refused for default, a repository with packages, or a virtual member)", "AdminRepositories", "DeleteRepository", Status = 204)]
    public async Task<IActionResult> Delete(string name)
    {
        ThrowIfClosed();
        if (await _administration.FindAsync(name, Cancellation) == null) return ApiProblems.NotFound($"repository '{name}'");

        var referencedBy = await Service<IVirtualRepositoryAdministration>().ReferencedByAsync(name, Cancellation);
        if (referencedBy.Count > 0) throw new RepositoryReferencedException(name, referencedBy);

        await _administration.DeleteAsync(name, Cancellation);

        return Done();
    }

    /// <summary>Replaces a remote repository's upstream.</summary>
    [HttpPut("{name}/remote")]
    [ApiOperation("Change a remote repository's upstream and settings", "AdminRepositories", "Edit", Confirm = true, Response = typeof(RemoteSettingsDto))]
    public async Task<IActionResult> UpdateRemote(string name, [FromBody] RemoteSettingsRequest request)
    {
        ThrowIfClosed();
        var remotes = Service<IRemoteRepositoryAdministration>();
        if (await remotes.FindAsync(name, Cancellation) == null) return ApiProblems.NotFound($"remote repository '{name}'");
        var refusal = await RequireConfirmationAsync();
        if (refusal != null) return refusal;

        await remotes.UpdateAsync(name, request.ToSettings(), request.ClearPassword, Cancellation, Actor);

        return Respond(ToDto((await remotes.FindAsync(name, Cancellation))!));
    }

    /// <summary>Asks an upstream whether it answers; saves nothing.</summary>
    [HttpPost("{name}/remote/test")]
    [ApiOperation("Test a remote repository's upstream with unsaved settings (saves nothing)", "AdminRepositories", "Edit", Confirm = true, Response = typeof(RemoteConnectionResult))]
    public async Task<IActionResult> TestRemote(string name, [FromBody] RemoteSettingsRequest request)
    {
        var remotes = Service<IRemoteRepositoryAdministration>();
        var detail = await remotes.FindAsync(name, Cancellation);
        if (detail == null) return ApiProblems.NotFound($"remote repository '{name}'");
        var refusal = await RequireConfirmationAsync();
        if (refusal != null) return refusal;

        var settings = request.ToSettings();
        if (detail.IsMirror)
        {
            settings.Url = detail.Settings.Url;
            settings.Protocol = detail.Settings.Protocol;
        }

        return Respond(await remotes.TestConnectionAsync(name, settings, Cancellation));
    }

    /// <summary>What a remote repository stores and remembers.</summary>
    [HttpGet("{name}/remote/cache")]
    [ApiOperation("Show what a remote repository stores and remembers", "AdminRepositories", "Edit", Response = typeof(RemoteCacheOverview))]
    public async Task<IActionResult> Cache(string name)
    {
        var remotes = Service<IRemoteRepositoryAdministration>();
        if (await remotes.FindAsync(name, Cancellation) == null) return ApiProblems.NotFound($"remote repository '{name}'");

        return Respond(await remotes.GetCacheAsync(name, Cancellation));
    }

    /// <summary>Deletes everything a remote repository stored.</summary>
    [HttpPost("{name}/remote/purge")]
    [ApiOperation("Purge a remote repository's cache (deletes what it stored)", "AdminRepositories", "Edit", Confirm = true, Response = typeof(PurgeResult))]
    public async Task<IActionResult> Purge(string name)
    {
        ThrowIfClosed();
        var remotes = Service<IRemoteRepositoryAdministration>();
        if (await remotes.FindAsync(name, Cancellation) == null) return ApiProblems.NotFound($"remote repository '{name}'");
        var refusal = await RequireConfirmationAsync();
        if (refusal != null) return refusal;

        return Respond(await remotes.PurgeAsync(name, Cancellation, Actor));
    }

    /// <summary>Deletes one cached package of a remote repository.</summary>
    [HttpPost("{name}/remote/purge-package")]
    [ApiOperation("Purge one cached package of a remote repository", "AdminRepositories", "Edit", Confirm = true, Response = typeof(PurgeResult))]
    public async Task<IActionResult> PurgePackage(string name, [FromBody] PurgePackageRequest request)
    {
        ThrowIfClosed();
        var remotes = Service<IRemoteRepositoryAdministration>();
        var found = await remotes.FindAsync(name, Cancellation);
        if (found == null) return ApiProblems.NotFound($"remote repository '{name}'");
        var refusal = await RequireConfirmationAsync();
        if (refusal != null) return refusal;

        var id = request.PackageId?.Trim();
        if (string.IsNullOrEmpty(id)) return ApiProblems.BadRequest("'packageId' is required.");

        var versionText = string.IsNullOrWhiteSpace(request.Version) ? null : request.Version.Trim();
        switch (found.Repository.Format)
        {
            case RepositoryFormat.Maven:
                return Respond(await remotes.PurgeArtifactAsync(name, id, versionText, Cancellation, Actor));
            case RepositoryFormat.Docker:
                return Respond(await remotes.PurgeImageAsync(name, id, versionText, Cancellation, Actor));
            default:
                NuGet.Versioning.NuGetVersion? version = null;
                if (versionText != null)
                {
                    if (!NuGet.Versioning.NuGetVersion.TryParse(versionText, out version)) return ApiProblems.BadRequest($"'{versionText}' is not a valid version.");
                }

                return Respond(await remotes.PurgePackageAsync(name, id, version, Cancellation, Actor));
        }
    }

    /// <summary>Replaces a virtual repository's members and deploy repository.</summary>
    [HttpPut("{name}/virtual")]
    [ApiOperation("Replace a virtual repository's members, their order and its deploy repository", "AdminRepositories", "Edit", Response = typeof(VirtualDto))]
    public async Task<IActionResult> UpdateVirtual(string name, [FromBody] VirtualRequest request)
    {
        ThrowIfClosed();
        var virtuals = Service<IVirtualRepositoryAdministration>();
        if (await virtuals.FindAsync(name, Cancellation) == null) return ApiProblems.NotFound($"virtual repository '{name}'");

        await virtuals.UpdateAsync(name, request.ToSettings(), Cancellation, Actor);
        var detail = (await virtuals.FindAsync(name, Cancellation))!;

        return Respond(new VirtualDto(
            detail.Members.Select(m => new VirtualMemberDto(m.Name, m.Exists)).ToList(),
            detail.DeployRepository,
            detail.DeployTarget?.Name,
            detail.EffectiveMembers.Select(m => m.Name).ToList()));
    }

    /// <summary>Which member would answer for a package version.</summary>
    [HttpPost("{name}/virtual/preview")]
    [ApiOperation("Show where a package version would come from in a virtual repository (fetches and stores nothing)", "AdminRepositories", "Edit", Response = typeof(PreviewDto))]
    public async Task<IActionResult> Preview(string name, [FromBody] PreviewRequest request)
    {
        var virtuals = Service<IVirtualRepositoryAdministration>();
        var detail = await virtuals.FindAsync(name, Cancellation);
        if (detail == null) return ApiProblems.NotFound($"virtual repository '{name}'");

        var id = request.PackageId?.Trim();
        if (string.IsNullOrEmpty(id)) return ApiProblems.BadRequest("'packageId' is required.");
        if (string.IsNullOrWhiteSpace(request.Version)) return ApiProblems.BadRequest("'version' is required.");

        var preview = await virtuals.PreviewAsync(name, id, request.Version.Trim(), Cancellation);

        return Respond(new PreviewDto(
            preview.PackageId,
            preview.Version,
            preview.Steps.Select(s => new PreviewStepDto(s.Member.Name, s.Outcome)).ToList(),
            preview.Answers?.Name));
    }

    private RepositoryDto Dto(RepositorySummary r)
        => new(r.Key, r.Name, r.Format, r.Kind, r.AnonymousRead, r.Created, r.PackageCount, FeedUrl(r.Name, r.Format));

    private string FeedUrl(string name, RepositoryFormat format)
    {
        var feedPath = _formats.Find(format)?.FeedPath ?? "v3/index.json";
        var generator = Service<IUrlGenerator>();
        var root = generator.GetServiceIndexUrl() ?? string.Empty;
        const string index = "v3/index.json";
        var prefix = root.EndsWith(index, StringComparison.Ordinal) ? root.Substring(0, root.Length - index.Length) : root.TrimEnd('/') + "/";

        return feedPath.Contains("{repo}", StringComparison.Ordinal)
            ? prefix + feedPath.Replace("{repo}", name)
            : (name == Repository.DefaultName ? prefix : $"{prefix}r/{name}/") + feedPath;
    }

    private static RemoteSettingsDto ToDto(RemoteRepositoryDetail d)
        => new(d.Settings.Url, d.Settings.Protocol.ToString(), d.Settings.Username, d.HasPassword, d.Settings.MetadataTtlSeconds, d.Settings.NegativeTtlSeconds,
            d.Settings.DownloadTimeoutSeconds, d.Settings.Offline, d.Settings.StoreLocally, d.IsMirror);

    private static bool TryParse<T>(string? text, T fallback, out T value) where T : struct, Enum
    {
        value = fallback;

        return string.IsNullOrWhiteSpace(text) || Enum.TryParse(text, ignoreCase: true, out value);
    }
}
