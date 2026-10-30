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
/// The repositories screen of the administrator: lists the repositories with their feed addresses and creates new ones: local, remote (an
/// upstream cache, see <see cref="RemoteRepositorySettings"/>) or virtual (an ordered group of members, filled in on its edit screen), of any
/// format whose module offers the kind; the name is validated by <see cref="RepositoryNames"/>. Creating is refused with 503 while the
/// <see cref="MaintenanceGate"/> is closed. Creating a remote repository, which can carry credentials, asks for the administrator's password
/// again. A refusal shows the screen again (<see cref="ScreenStatus.Invalid"/>, the reason in <see cref="RepositoriesViewModel.Error"/>) with what
/// was typed, except the upstream password, which no answer carries.
/// </summary>
public sealed class AdminRepositoriesUseCase : AdminUseCaseBase
{
    private static readonly ScreenRef ListScreen = ScreenCatalog.AdminRepositories;
    private static readonly ScreenRef EditScreen = ScreenCatalog.AdminRepositoryEdit;

    private readonly IRepositoryService _repositories;
    private readonly IRepositoryAdministration _administration;
    private readonly IVirtualRepositoryAdministration _virtuals;
    private readonly FormatRegistry _formats;
    private readonly RepositoryFeedUrl _feeds;

    /// <summary>Creates the use case.</summary>
    public AdminRepositoriesUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IRepositoryService repositories,
        IRepositoryAdministration administration,
        IVirtualRepositoryAdministration virtuals,
        FormatRegistry formats,
        RepositoryFeedUrl feeds,
        MaintenanceGate? gate = null)
        : base(users, confirmation, gate)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _administration = administration ?? throw new ArgumentNullException(nameof(administration));
        _virtuals = virtuals ?? throw new ArgumentNullException(nameof(virtuals));
        _formats = formats ?? throw new ArgumentNullException(nameof(formats));
        _feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
    }

    /// <summary>
    /// Lists the repositories, narrowed by <paramref name="format"/>, <paramref name="kind"/> and <paramref name="name"/> (a name fragment) like
    /// <c>GET /api/v1/repositories</c>, through <see cref="RepositorySummaryFilter"/>.
    /// </summary>
    /// <param name="caller">The signed-in administrator.</param>
    /// <param name="format">Only this format, or blank.</param>
    /// <param name="kind">Only this kind, or blank.</param>
    /// <param name="name">Only names containing this, or blank.</param>
    /// <param name="returnPage">The address to come back to after signing in again.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<ScreenResult<RepositoriesViewModel>> GetAsync(ClaimsPrincipal caller, string? format, string? kind, string? name, string? returnPage, CancellationToken cancellationToken)
    {
        var model = new RepositoriesViewModel
        {
            FilterFormat = Clean(format),
            FilterKind = Clean(kind),
            FilterName = Clean(name),
        };

        await LoadAsync(model, caller, returnPage, cancellationToken);
        model.Repositories = RepositorySummaryFilter.Apply(model.Repositories, model.FilterFormat, model.FilterKind, model.FilterName).ToList();
        FillAddresses(model);

        return ScreenResult<RepositoriesViewModel>.Ok(model);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Creates a local NuGet repository (reads <c>Name</c>).</summary>
    public async Task<ScreenResult<RepositoriesViewModel>> CreateAsync(ClaimsPrincipal caller, RepositoryCreateForm? form, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = Start(form);
        var name = model.Name?.Trim();
        if (!RepositoryNames.IsValid(name, out var invalid))
        {
            return await RefuseAsync(model, caller, returnPage, invalid, cancellationToken);
        }

        try
        {
            await _repositories.CreateAsync(name!, RepositoryFormat.NuGet, RepositoryKind.Local, cancellationToken);
        }
        catch (Exception e) when (e is NotSupportedException or RepositoryExistsException)
        {
            return await RefuseAsync(model, caller, returnPage, e.Message, cancellationToken);
        }

        return Back<RepositoriesViewModel>(ListScreen, null, $"Repository {name} was created.", null);
    }

    /// <summary>
    /// Creates a local repository of the format named by <c>Format</c> from <c>Name</c>, <c>AnonymousRead</c> (default on) and the format
    /// module's <see cref="IFormatModule.CreateFields"/> in <c>Fields</c>. Not found for a format with no module (or NuGet, which has its own form).
    /// </summary>
    public async Task<ScreenResult<RepositoriesViewModel>> CreateFormatAsync(ClaimsPrincipal caller, RepositoryCreateForm? form, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = Start(form);
        var module = _formats.Modules.FirstOrDefault(m => string.Equals(m.Format.ToString(), form?.Format, StringComparison.OrdinalIgnoreCase) && m.Format != RepositoryFormat.NuGet);
        if (module == null || !module.CreatableKinds.Contains(RepositoryKind.Local)) return ScreenResult<RepositoriesViewModel>.NotFound();

        model.Format = module.Format.ToString();
        var name = model.Name?.Trim();
        if (!RepositoryNames.IsValid(name, out var invalid))
        {
            return await RefuseAsync(model, caller, returnPage, invalid, cancellationToken);
        }

        var fields = (form?.Fields ?? new Dictionary<string, string?>()).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        try
        {
            await _repositories.CreateAsync(name!, module.Format, RepositoryKind.Local, fields, form?.AnonymousRead ?? true, cancellationToken);
        }
        catch (Exception e) when (e is NotSupportedException or RepositoryExistsException or ArgumentException)
        {
            return await RefuseAsync(model, caller, returnPage, e.Message, cancellationToken);
        }

        return Back<RepositoriesViewModel>(ListScreen, null, $"{module.DisplayName} repository {name} was created.", null);
    }

    /// <summary>
    /// Creates a virtual repository with no members (reads <c>Name</c>, <c>Format</c> (NuGet when blank) and <c>AnonymousRead</c>, default on); the
    /// members are added on its screen. Not found for a format whose module does not support virtual repositories.
    /// </summary>
    public async Task<ScreenResult<RepositoriesViewModel>> CreateVirtualAsync(ClaimsPrincipal caller, RepositoryCreateForm? form, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = Start(form);
        var module = ModuleOf(form?.Format, m => m.SupportsVirtual);
        if (module == null) return ScreenResult<RepositoriesViewModel>.NotFound();

        var name = model.Name?.Trim();
        if (!RepositoryNames.IsValid(name, out var invalid))
        {
            return await RefuseAsync(model, caller, returnPage, invalid, cancellationToken);
        }

        try
        {
            await _virtuals.CreateAsync(name!, new VirtualRepositorySettings(), cancellationToken, caller.Identity?.Name, form?.AnonymousRead ?? true, module.Format);
        }
        catch (Exception e) when (e is NotSupportedException or RepositoryExistsException or ArgumentException)
        {
            return await RefuseAsync(model, caller, returnPage, e.Message, cancellationToken);
        }

        return Back<RepositoriesViewModel>(EditScreen, Route(("name", name)), $"Virtual {Describe(module)}repository {name} was created. Add its members below.", null);
    }

    /// <summary>
    /// Creates a remote repository from <c>Name</c>, <c>Format</c> (NuGet when blank), the upstream in <c>Remote</c>, <c>AnonymousRead</c> (null:
    /// anyone unless credentials are set) and the administrator's <c>Password</c>.
    /// </summary>
    public async Task<ScreenResult<RepositoriesViewModel>> CreateRemoteAsync(ClaimsPrincipal caller, RepositoryCreateForm? form, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = Start(form);
        var module = ModuleOf(form?.Format, m => m.CreatableKinds.Contains(RepositoryKind.Remote));
        if (module == null) return ScreenResult<RepositoriesViewModel>.NotFound();
        model.Format = module.Format.ToString();

        var current = await CurrentAsync(caller);
        if (current == null || !await Confirmation.IsConfirmedAsync(current, form?.Password))
        {
            return await RefuseAsync(model, caller, returnPage, NotConfirmed, cancellationToken);
        }

        var name = model.Name?.Trim();
        if (!RepositoryNames.IsValid(name, out var invalid))
        {
            return await RefuseAsync(model, caller, returnPage, invalid, cancellationToken);
        }

        try
        {
            // Absent = the default: readable by anonymous clients only when no credentials are configured.
            var settings = model.Remote.ToSettings();
            var anonymous = form?.AnonymousRead ?? !settings.HasCredentials;
            await _repositories.CreateAsync(name!, module.Format, RepositoryKind.Remote, settings, anonymous, cancellationToken);
        }
        catch (Exception e) when (e is NotSupportedException or RepositoryExistsException or ArgumentException or InvalidOperationException)
        {
            return await RefuseAsync(model, caller, returnPage, e.Message, cancellationToken);
        }

        return Back<RepositoriesViewModel>(EditScreen, Route(("name", name)), $"Remote {Describe(module)}repository {name} was created.", null);
    }

    private static RepositoriesViewModel Start(RepositoryCreateForm? form)
    {
        var model = new RepositoriesViewModel { Name = form?.Name };
        if (form?.Remote != null) model.Remote = form.Remote;

        return model;
    }

    // "npm " for a format other than NuGet, whose wording predates the format layer ("Remote repository up was created.").
    private static string Describe(IFormatModule module) => module.Format == RepositoryFormat.NuGet ? string.Empty : module.DisplayName + " ";

    // The module named by a posted format (NuGet when none), when it passes 'offers'; null otherwise.
    private IFormatModule? ModuleOf(string? format, Func<IFormatModule, bool> offers)
    {
        var module = string.IsNullOrEmpty(format)
            ? _formats.Find(RepositoryFormat.NuGet)
            : _formats.Modules.FirstOrDefault(m => string.Equals(m.Format.ToString(), format, StringComparison.OrdinalIgnoreCase));

        return module != null && offers(module) ? module : null;
    }

    // Shows the screen again with a reason, keeping what was typed (the password excepted).
    private async Task<ScreenResult<RepositoriesViewModel>> RefuseAsync(RepositoriesViewModel model, ClaimsPrincipal caller, string? returnPage, string? error, CancellationToken cancellationToken)
    {
        model.Error = error;
        await LoadAsync(model, caller, returnPage, cancellationToken);
        FillAddresses(model);

        return ScreenResult<RepositoriesViewModel>.Invalid(model, error ?? "The repository was not created.");
    }

    private async Task LoadAsync(RepositoriesViewModel model, ClaimsPrincipal caller, string? returnPage, CancellationToken cancellationToken)
    {
        model.Remote.Password = null; // never sent back

        await Confirmation.LoadAsync(model, caller, returnPage);
        model.CanCreate = _repositories.SupportsMultipleRepositories;
        model.AllFormats = _formats.Modules.Select(RepositoryFormatChoice.Of).ToList();
        model.RemoteFormats = _formats.Modules.Where(m => m.CreatableKinds.Contains(RepositoryKind.Remote)).Select(RepositoryFormatChoice.Of).ToList();
        model.VirtualFormats = _formats.Modules.Where(m => m.SupportsVirtual).Select(RepositoryFormatChoice.Of).ToList();
        model.OtherFormats = _formats.Modules.Where(m => m.Format != RepositoryFormat.NuGet && m.CreatableKinds.Contains(RepositoryKind.Local)).Select(RepositoryFormatChoice.Of).ToList();

        model.Repositories = await _administration.ListAsync(cancellationToken);
        model.Deployable = await DeployableAsync(model.Repositories, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> DeployableAsync(IReadOnlyList<RepositorySummary> repositories, CancellationToken cancellationToken)
    {
        var deployable = new List<string>();
        foreach (var repository in repositories.Where(r => r.Kind == RepositoryKind.Virtual))
        {
            if ((await _virtuals.FindAsync(repository.Name, cancellationToken))?.DeployTarget != null) deployable.Add(repository.Name);
        }

        return deployable;
    }

    // The feed address, base address and links of each listed repository.
    private void FillAddresses(RepositoriesViewModel model)
    {
        var feeds = new Dictionary<string, string>(StringComparer.Ordinal);
        var bases = new Dictionary<string, string>(StringComparer.Ordinal);
        var links = new Dictionary<string, IReadOnlyList<FormatLink>>(StringComparer.Ordinal);
        foreach (var repository in model.Repositories)
        {
            var module = _formats.Find(repository.Format);
            feeds[repository.Name] = _feeds.For(repository.Name, module?.FeedPath ?? "v3/index.json");
            bases[repository.Name] = _feeds.BaseFor(repository.Name);

            var takesUploads = repository.Kind switch
            {
                RepositoryKind.Remote => false,
                RepositoryKind.Virtual => model.Deployable.Contains(repository.Name),
                _ => true,
            };
            links[repository.Name] = (module?.RepositoryLinks(new RepositoryInfo(repository.Key, repository.Name, repository.Format, repository.Kind)) ?? Array.Empty<FormatLink>())
                .Where(l => !l.NeedsUploads || takesUploads)
                .ToList();
        }

        model.Feeds = feeds;
        model.BaseUrls = bases;
        model.Links = links;
    }
}
