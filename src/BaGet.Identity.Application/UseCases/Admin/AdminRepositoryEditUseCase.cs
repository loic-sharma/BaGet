using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using BaGet.Identity.Application.Models.Repositories;

namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>
/// The screen of one repository (<c>/admin/repositories/{name}</c>): shows its feed address and the access model's view of it (who effectively holds
/// each action) and edits what can change: whether anonymous requests may read it. The name never changes (it is in URLs and storage paths), and neither
/// does the format or kind. A remote repository also shows and edits its upstream (URL, protocol, credentials, cache periods, offline, store locally),
/// tests the connection with the unsaved values, shows its cache and purges it (whole, or one package). Saving and purging are refused with 503 while
/// the <see cref="MaintenanceGate"/> is closed. Changing, purging and testing the connection of a remote repository ask for the administrator's
/// password again (a test saves nothing, but makes the server send a request to a typed URL, with the stored password when the origin is unchanged). A
/// virtual repository shows and edits its members (add, remove, move up or down), its deploy repository, and previews where a package version would
/// come from (<see cref="IVirtualRepositoryAdministration.PreviewAsync(string, string, string, CancellationToken)"/>); those edits are saved at once,
/// refused with 503 while the gate is closed. A local Maven repository edits its policy.
/// <para>
/// A refusal shows the screen again (<see cref="ScreenStatus.Invalid"/>, the reason in <see cref="RepositoryEditViewModel.Error"/>). No answer carries a
/// password: the upstream's stored password is never read back (only <c>HasPassword</c>) and the one typed is cleared from the model.
/// </para>
/// </summary>
public sealed class AdminRepositoryEditUseCase : AdminUseCaseBase
{
    private static readonly ScreenRef ListScreen = ScreenCatalog.AdminRepositories;
    private static readonly ScreenRef EditScreen = ScreenCatalog.AdminRepositoryEdit;

    private readonly IRepositoryAdministration _administration;
    private readonly IRepositoryService _repositories;
    private readonly IRemoteRepositoryAdministration _remotes;
    private readonly IVirtualRepositoryAdministration _virtuals;
    private readonly IMavenRepositoryAdministration _maven;
    private readonly IAccessEvaluator _access;
    private readonly FormatRegistry _formats;
    private readonly RepositoryFeedUrl _feeds;

    /// <summary>Creates the use case.</summary>
    public AdminRepositoryEditUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IRepositoryAdministration administration,
        IRepositoryService repositories,
        IRemoteRepositoryAdministration remotes,
        IVirtualRepositoryAdministration virtuals,
        IMavenRepositoryAdministration maven,
        IAccessEvaluator access,
        FormatRegistry formats,
        RepositoryFeedUrl feeds,
        MaintenanceGate? gate = null)
        : base(users, confirmation, gate)
    {
        _administration = administration ?? throw new ArgumentNullException(nameof(administration));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _remotes = remotes ?? throw new ArgumentNullException(nameof(remotes));
        _virtuals = virtuals ?? throw new ArgumentNullException(nameof(virtuals));
        _maven = maven ?? throw new ArgumentNullException(nameof(maven));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _formats = formats ?? throw new ArgumentNullException(nameof(formats));
        _feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
    }

    /// <summary>Shows the repository; not found when there is none.</summary>
    /// <param name="caller">The signed-in administrator.</param>
    /// <param name="name">The repository's name.</param>
    /// <param name="returnPage">The address to come back to after signing in again.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<ScreenResult<RepositoryEditViewModel>> GetAsync(ClaimsPrincipal caller, string? name, string? returnPage, CancellationToken cancellationToken)
    {
        var model = Start(name, null, returnPage);
        if (!await LoadAsync(model, caller, cancellationToken)) return ScreenResult<RepositoryEditViewModel>.NotFound();

        model.AnonymousRead = model.Repository!.AnonymousRead;
        if (model.RemoteDetail != null) model.Remote = RemoteFormModel.From(model.RemoteDetail);

        return Done(model);
    }

    /// <summary>Saves the repository: reads <c>AnonymousRead</c> and, for a remote one, <c>Remote</c> (a blank password keeps the stored one) and <c>Password</c>.</summary>
    public async Task<ScreenResult<RepositoryEditViewModel>> SaveAsync(ClaimsPrincipal caller, string? name, RepositoryEditRequest? request, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = Start(name, request, returnPage);
        var summary = await _administration.FindAsync(model.Name, cancellationToken);
        if (summary == null) return ScreenResult<RepositoryEditViewModel>.NotFound();

        if (summary.Kind == RepositoryKind.Remote)
        {
            if (!await ConfirmedAsync(caller, request?.Password)) return await RefuseAsync(model, caller, NotConfirmed, cancellationToken);

            try
            {
                await _remotes.UpdateAsync(model.Name!, model.Remote.ToSettings(), model.Remote.ClearPassword, cancellationToken, caller.Identity?.Name);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                return await RefuseAsync(model, caller, e.Message, cancellationToken);
            }
        }

        await _administration.SetAnonymousReadAsync(model.Name!, model.AnonymousRead, cancellationToken, caller.Identity?.Name);

        return Back<RepositoryEditViewModel>(ListScreen, null, $"Repository {model.Name} was saved.", null);
    }

    /// <summary>
    /// Saves the policy of a local Maven repository (<c>Policy</c>, <c>Redeploy</c>, <c>SnapshotBuilds</c>): which versions it accepts, whether a release may
    /// be redeployed and how many snapshot builds are kept. Audited; a stricter policy applies to later deploys and deletes nothing.
    /// </summary>
    public async Task<ScreenResult<RepositoryEditViewModel>> SaveMavenSettingsAsync(ClaimsPrincipal caller, string? name, RepositoryEditRequest? request, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = Start(name, request, returnPage);
        MavenRepositorySettings settings;
        try
        {
            settings = MavenRepositorySettings.FromFields(new Dictionary<string, string?>
            {
                ["policy"] = request?.Policy,
                ["redeploy"] = request?.Redeploy,
                ["snapshotBuilds"] = request?.SnapshotBuilds,
            });
        }
        catch (ArgumentException e)
        {
            return await RefuseAsync(model, caller, e.Message, cancellationToken);
        }

        if (!await _maven.SaveSettingsAsync(model.Name!, settings, caller.Identity?.Name, cancellationToken))
        {
            return ScreenResult<RepositoryEditViewModel>.NotFound();
        }

        return Back<RepositoryEditViewModel>(EditScreen, Route(("name", model.Name)), $"The settings of repository {model.Name} were saved.", null);
    }

    /// <summary>Asks the upstream with the unsaved <c>Remote</c> values (a blank password means the stored one) and saves nothing; asks for the <c>Password</c>.</summary>
    public async Task<ScreenResult<RepositoryEditViewModel>> TestAsync(ClaimsPrincipal caller, string? name, RepositoryEditRequest? request, string? returnPage, CancellationToken cancellationToken)
    {
        var model = Start(name, request, returnPage);
        if (!await LoadAsync(model, caller, cancellationToken)) return ScreenResult<RepositoryEditViewModel>.NotFound();
        if (model.RemoteDetail == null) return ScreenResult<RepositoryEditViewModel>.NotFound();

        // The probe makes this server send a request to a URL the administrator typed (and the stored password, if the
        // origin is unchanged): it asks for the password again, like the changes that hand out credentials.
        if (!await ConfirmedAsync(caller, request?.Password)) return await RefuseAsync(model, caller, NotConfirmed, cancellationToken);

        model.AnonymousRead = model.Repository!.AnonymousRead;
        var settings = model.Remote.ToSettings();
        if (model.RemoteDetail.IsMirror)
        {
            settings.Url = model.RemoteDetail.Settings.Url;
            settings.Protocol = model.RemoteDetail.Settings.Protocol;
        }

        model.TestResult = await _remotes.TestConnectionAsync(model.Name, settings, cancellationToken);
        KeepRemoteFields(model);

        return Done(model);
    }

    /// <summary>Deletes every package the remote repository stored and forgets what it remembered of its upstream; asks for the <c>Password</c>.</summary>
    public async Task<ScreenResult<RepositoryEditViewModel>> PurgeAsync(ClaimsPrincipal caller, string? name, RepositoryEditRequest? request, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = Start(name, request, returnPage);
        var found = await _remotes.FindAsync(model.Name, cancellationToken);
        if (found == null) return ScreenResult<RepositoryEditViewModel>.NotFound();
        if (!await ConfirmedAsync(caller, request?.Password)) return await RefuseAsync(model, caller, NotConfirmed, cancellationToken);

        var result = await _remotes.PurgeAsync(model.Name!, cancellationToken, caller.Identity?.Name);
        var unit = (await _remotes.FindAsync(model.Name, cancellationToken))?.Repository.Format == RepositoryFormat.Docker ? "manifest(s)" : "package version(s)";

        return Back<RepositoryEditViewModel>(
            EditScreen,
            Route(("name", model.Name)),
            $"The cache of {model.Name} was purged: {result.Packages} {unit} deleted, {result.CachedAnswers} remembered answer(s) dropped.",
            null);
    }

    /// <summary>
    /// Deletes one cached package (<c>PackageId</c>; every version, or the <c>PackageVersion</c> given) and forgets what was remembered about it; asks for
    /// the <c>Password</c>.
    /// </summary>
    public async Task<ScreenResult<RepositoryEditViewModel>> PurgePackageAsync(ClaimsPrincipal caller, string? name, RepositoryEditRequest? request, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = Start(name, request, returnPage);
        var found = await _remotes.FindAsync(model.Name, cancellationToken);
        if (found == null) return ScreenResult<RepositoryEditViewModel>.NotFound();
        if (!await ConfirmedAsync(caller, request?.Password)) return await RefuseAsync(model, caller, NotConfirmed, cancellationToken);

        var id = model.PackageId?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            return await RefuseAsync(model, caller, "Enter the id of the package to purge.", cancellationToken);
        }

        // A Maven version is any version folder name, not always a NuGet version (5.3.20.RELEASE), and a Docker one is a tag or a digest:
        // they travel as text.
        var isMaven = found.Repository.Format == RepositoryFormat.Maven;
        var isDocker = found.Repository.Format == RepositoryFormat.Docker;
        NuGet.Versioning.NuGetVersion? version = null;
        string? versionText = string.IsNullOrWhiteSpace(model.PackageVersion) ? null : model.PackageVersion.Trim();
        if (!isMaven && !isDocker && versionText != null)
        {
            if (!NuGet.Versioning.NuGetVersion.TryParse(versionText, out version))
            {
                return await RefuseAsync(model, caller, $"'{model.PackageVersion}' is not a valid version.", cancellationToken);
            }

            versionText = version.ToNormalizedString();
        }

        PurgeResult result;
        try
        {
            result = isMaven
                ? await _remotes.PurgeArtifactAsync(model.Name!, id, versionText, cancellationToken, caller.Identity?.Name)
                : isDocker
                    ? await _remotes.PurgeImageAsync(model.Name!, id, versionText, cancellationToken, caller.Identity?.Name)
                    : await _remotes.PurgePackageAsync(model.Name!, id, version, cancellationToken, caller.Identity?.Name);
        }
        catch (ArgumentException e)
        {
            // A name the repository's format would not accept (an npm name must be lower case...).
            return await RefuseAsync(model, caller, e.Message, cancellationToken);
        }

        var shown = versionText == null ? string.Empty : " " + versionText;
        var message = result.Packages == 0 && result.CachedAnswers == 0
            ? $"Nothing was cached for {id}{shown}."
            : $"{id}{shown} was purged: {result.Packages} {(isMaven ? "file(s)" : isDocker ? "manifest(s)" : "package version(s)")} deleted, {result.CachedAnswers} remembered answer(s) dropped.";

        return Back<RepositoryEditViewModel>(EditScreen, Route(("name", model.Name)), message, null);
    }

    /// <summary>Adds the repository <c>Member</c> at the end of the virtual repository's members.</summary>
    public Task<ScreenResult<RepositoryEditViewModel>> AddMemberAsync(ClaimsPrincipal caller, string? name, RepositoryEditRequest? request, string? returnPage, CancellationToken cancellationToken)
    {
        var member = request?.Member;

        return ChangeMembersAsync(
            caller, name, request, returnPage,
            settings =>
            {
                if (string.IsNullOrEmpty(member)) return "Choose the repository to add.";
                if (settings.Members.Contains(member!)) return $"'{member}' is already a member.";

                settings.Members.Add(member!);

                return null;
            },
            $"{member} was added.",
            cancellationToken);
    }

    /// <summary>Removes the <c>Member</c> (and the deploy repository too when it was that member).</summary>
    public Task<ScreenResult<RepositoryEditViewModel>> RemoveMemberAsync(ClaimsPrincipal caller, string? name, RepositoryEditRequest? request, string? returnPage, CancellationToken cancellationToken)
    {
        var member = request?.Member;

        return ChangeMembersAsync(
            caller, name, request, returnPage,
            settings =>
            {
                if (!settings.Members.Remove(member ?? string.Empty)) return $"'{member}' is not a member.";
                if (settings.DeployRepository == member) settings.DeployRepository = null;

                return null;
            },
            $"{member} was removed.",
            cancellationToken);
    }

    /// <summary>Moves the <c>Member</c> one place earlier (<c>Direction</c> <c>up</c>) or later (otherwise) in the resolution order.</summary>
    public Task<ScreenResult<RepositoryEditViewModel>> MoveMemberAsync(ClaimsPrincipal caller, string? name, RepositoryEditRequest? request, string? returnPage, CancellationToken cancellationToken)
    {
        var member = request?.Member;
        var direction = request?.Direction;

        return ChangeMembersAsync(
            caller, name, request, returnPage,
            settings =>
            {
                var index = settings.Members.IndexOf(member ?? string.Empty);
                if (index < 0) return $"'{member}' is not a member.";

                var target = string.Equals(direction, "up", StringComparison.OrdinalIgnoreCase) ? index - 1 : index + 1;
                if (target < 0 || target >= settings.Members.Count) return null;

                (settings.Members[index], settings.Members[target]) = (settings.Members[target], settings.Members[index]);

                return null;
            },
            $"{member} was moved {(string.Equals(direction, "up", StringComparison.OrdinalIgnoreCase) ? "up" : "down")}.",
            cancellationToken);
    }

    /// <summary>Sets the member that receives uploads (<c>Deploy</c>; blank: the virtual repository takes none).</summary>
    public Task<ScreenResult<RepositoryEditViewModel>> SetDeployAsync(ClaimsPrincipal caller, string? name, RepositoryEditRequest? request, string? returnPage, CancellationToken cancellationToken)
    {
        var deploy = request?.Deploy;

        return ChangeMembersAsync(
            caller, name, request, returnPage,
            settings =>
            {
                settings.DeployRepository = string.IsNullOrWhiteSpace(deploy) ? null : deploy;

                return null;
            },
            string.IsNullOrWhiteSpace(deploy) ? "The virtual repository no longer takes uploads." : $"Uploads now go to {deploy}.",
            cancellationToken);
    }

    /// <summary>Shows where the package version typed (<c>PreviewPackageId</c>, <c>PreviewVersion</c>) would come from, member by member (nothing is fetched or stored).</summary>
    public async Task<ScreenResult<RepositoryEditViewModel>> PreviewAsync(ClaimsPrincipal caller, string? name, RepositoryEditRequest? request, string? returnPage, CancellationToken cancellationToken)
    {
        var model = Start(name, request, returnPage);
        if (!await LoadAsync(model, caller, cancellationToken) || model.VirtualDetail == null) return ScreenResult<RepositoryEditViewModel>.NotFound();

        model.AnonymousRead = model.Repository!.AnonymousRead;
        var id = model.PreviewPackageId?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            return Refused(model, "Enter the id of the package to resolve.");
        }

        var repository = model.Repository;
        var isMaven = repository.Format == RepositoryFormat.Maven;
        var isDocker = repository.Format == RepositoryFormat.Docker;
        if (!isMaven && !isDocker && !NuGet.Versioning.NuGetVersion.TryParse(model.PreviewVersion?.Trim(), out _))
        {
            return Refused(model, $"'{model.PreviewVersion}' is not a valid version.");
        }

        if (repository.Format == RepositoryFormat.Npm)
        {
            // An npm name is checked by the one rule every npm route and page uses (scoped names; a lookup accepts legacy upper case).
            if (!NpmNames.TryParseLookup(id, out id, out var invalidName))
            {
                return Refused(model, invalidName);
            }
        }

        try
        {
            model.Preview = await _virtuals.PreviewAsync(model.Name!, id, model.PreviewVersion?.Trim() ?? string.Empty, cancellationToken);
        }
        catch (ArgumentException e)
        {
            // An artifact that is not group:artifact, or a version that is not a folder name.
            return Refused(model, e.Message);
        }

        return Done(model);
    }

    // ------------------------------------------------------------------ shared

    // The model for a request: the route name and the posted fields.
    private static RepositoryEditViewModel Start(string? name, RepositoryEditRequest? request, string? returnPage)
    {
        var model = new RepositoryEditViewModel { Name = name };
        if (returnPage != null) model.ReturnPage = returnPage;
        if (request != null)
        {
            model.AnonymousRead = request.AnonymousRead;
            if (request.Remote != null) model.Remote = request.Remote;
            model.PreviewPackageId = request.PreviewPackageId;
            model.PreviewVersion = request.PreviewVersion;
            model.PackageId = request.PackageId;
            model.PackageVersion = request.PackageVersion;
        }

        return model;
    }

    // Loads the repository, its access summary and, for a remote one, its upstream and cache. False: no such repository.
    private async Task<bool> LoadAsync(RepositoryEditViewModel model, ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        await Confirmation.LoadAsync(model, caller, model.ReturnPage.Length == 0 ? null : model.ReturnPage);
        var repository = model.Repository = await _administration.FindAsync(model.Name, cancellationToken);
        if (repository == null) return false;

        var module = _formats.Find(repository.Format);
        var browse = module
            ?.RepositoryLinks(new RepositoryInfo(repository.Key, repository.Name, repository.Format, repository.Kind))
            .FirstOrDefault(l => l.Id == "browse" && l.Controller != null && l.Action != null);
        model.BrowseScreen = browse == null ? ScreenCatalog.Index : new ScreenRef(browse.Controller!, browse.Action!);
        model.FeedUrl = _feeds.For(repository.Name, module?.FeedPath ?? "v3/index.json");

        var info = (await _repositories.ListAsync(cancellationToken)).FirstOrDefault(r => r.Name == repository.Name);
        if (info != null)
        {
            model.Access = await _access.DescribeAsync(info, cancellationToken);
        }

        if (repository.Kind == RepositoryKind.Remote)
        {
            model.RemoteDetail = await _remotes.FindAsync(model.Name, cancellationToken);
            if (model.RemoteDetail != null) model.Cache = await _remotes.GetCacheAsync(model.Name!, cancellationToken);
        }
        else if (repository.Kind == RepositoryKind.Virtual)
        {
            model.VirtualDetail = await _virtuals.FindAsync(model.Name, cancellationToken);
            var members = model.VirtualDetail?.Members.Select(m => m.Name).ToHashSet() ?? new HashSet<string>();
            model.MemberCandidates = (await _virtuals.CandidatesAsync(model.Name, cancellationToken)).Where(c => !members.Contains(c.Name)).ToList();
        }

        if (repository.Kind != RepositoryKind.Virtual)
        {
            model.ReferencedBy = await _virtuals.ReferencedByAsync(repository.Name, cancellationToken);
        }

        if (repository.Format == RepositoryFormat.Maven && repository.Kind == RepositoryKind.Local)
        {
            model.MavenSettings = await _maven.GetSettingsAsync(repository.Name, cancellationToken);
        }

        if (repository.IsDefault)
        {
            model.MirrorRepository = await _remotes.FindAsync(Repository.MirrorName, cancellationToken);
            model.MirrorActive = model.MirrorRepository?.IsMirror == true;
        }

        return true;
    }

    private async Task<bool> ConfirmedAsync(ClaimsPrincipal caller, string? password)
    {
        var current = await CurrentAsync(caller);

        return current != null && await Confirmation.IsConfirmedAsync(current, password);
    }

    // Applies 'change' to a copy of the virtual repository's settings and saves them; 'change' returns a reason to refuse.
    private async Task<ScreenResult<RepositoryEditViewModel>> ChangeMembersAsync(
        ClaimsPrincipal caller,
        string? name,
        RepositoryEditRequest? request,
        string? returnPage,
        Func<VirtualRepositorySettings, string?> change,
        string done,
        CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = Start(name, request, returnPage);
        var detail = await _virtuals.FindAsync(model.Name, cancellationToken);
        if (detail == null) return ScreenResult<RepositoryEditViewModel>.NotFound();

        var settings = new VirtualRepositorySettings
        {
            Members = detail.Members.Select(m => m.Name).ToList(),
            DeployRepository = detail.DeployRepository,
        };

        var refusal = change(settings);
        if (refusal != null) return await RefuseAsync(model, caller, refusal, cancellationToken);

        try
        {
            await _virtuals.UpdateAsync(model.Name!, settings, cancellationToken, caller.Identity?.Name);
        }
        catch (ArgumentException e)
        {
            return await RefuseAsync(model, caller, e.Message, cancellationToken);
        }

        return ScreenResult<RepositoryEditViewModel>.GoTo(new ScreenNext(EditScreen, Route(("name", model.Name)), done, null, null, "repository-section-virtual"));
    }

    // Shows the screen again with a reason, keeping what was typed.
    private async Task<ScreenResult<RepositoryEditViewModel>> RefuseAsync(RepositoryEditViewModel model, ClaimsPrincipal caller, string error, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(model, caller, cancellationToken)) return ScreenResult<RepositoryEditViewModel>.NotFound();

        if (model.RemoteDetail != null) KeepRemoteFields(model);

        return Refused(model, error);
    }

    private static ScreenResult<RepositoryEditViewModel> Refused(RepositoryEditViewModel model, string? error)
    {
        model.Error = error;
        model.Remote.Password = null;

        return ScreenResult<RepositoryEditViewModel>.Invalid(model, error ?? "The change was refused.");
    }

    private static ScreenResult<RepositoryEditViewModel> Done(RepositoryEditViewModel model)
    {
        model.Remote.Password = null; // never sent back

        return ScreenResult<RepositoryEditViewModel>.Ok(model);
    }

    // The display-only fields of the upstream form, which a posted form does not carry; the typed password is never shown again.
    private static void KeepRemoteFields(RepositoryEditViewModel model)
    {
        model.Remote.HasPassword = model.RemoteDetail!.HasPassword;
        model.Remote.EndpointFromMirror = model.RemoteDetail.IsMirror;
        model.Remote.Format = model.RemoteDetail.Repository.Format;
        model.Remote.Password = null;
    }
}
