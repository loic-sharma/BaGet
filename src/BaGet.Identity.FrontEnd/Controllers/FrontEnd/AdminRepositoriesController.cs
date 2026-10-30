using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Repositories;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The administrator's repository screens: the list with the create forms (<c>/admin/repositories</c>), one repository
/// (<c>/admin/repositories/{name}</c>: anonymous read, upstream, cache, members, Maven settings) and its deletion
/// (<c>/admin/repositories/{name}/delete</c>). They replace the Razor Pages <c>/Admin/Repositories</c>,
/// <c>/Admin/RepositoryEdit</c> and <c>/Admin/RepositoryDelete</c> (same URLs, same <c>?handler=</c> values); the work is done by the JSON twin
/// (<see cref="AdminRepositoriesApiController"/>) and this controller only renders its answers.
/// Administrators only; every write is refused with 503 while the read-only gate is closed, and the changes that hand out
/// credentials or delete data ask for the administrator's password again, as the pages did.
/// </summary>
public sealed class AdminRepositoriesController : RepositoryControllerBase
{
    // ---------------------------------------------------------------- Repositories

    /// <summary>Lists the repositories (narrowed by <c>format</c>, <c>kind</c>, <c>q</c>) and shows the create forms.</summary>
    [Route("admin/repositories"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(string? format, string? kind, string? q, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoriesViewModel>(api => api.GetRepositories(format, kind, q, cancellationToken)), "Index");

    /// <summary>Creates a local NuGet repository.</summary>
    [Route("admin/repositories"), HttpPost, PageHandler]
    public async Task<IActionResult> Create(string? name, RemoteFormModel? remote, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoriesViewModel>(api => api.Create(new RepositoryCreateForm { Name = name, Remote = remote ?? new RemoteFormModel() }, cancellationToken)), "Index");

    /// <summary>Creates a local repository of a format other than NuGet from the posted <c>field:*</c> values.</summary>
    [Route("admin/repositories"), HttpPost, PageHandler("Format")]
    public async Task<IActionResult> CreateFormat(string? name, RemoteFormModel? remote, string? format, bool anonymousRead = true, CancellationToken cancellationToken = default)
        => Show(
            await Api<AdminRepositoriesApiController, RepositoriesViewModel>(api => api.CreateFormat(
                new RepositoryCreateForm { Name = name, Remote = remote ?? new RemoteFormModel(), Format = format, AnonymousRead = anonymousRead, Fields = PostedFields() },
                cancellationToken)),
            "Index");

    /// <summary>Creates a virtual repository with no members.</summary>
    [Route("admin/repositories"), HttpPost, PageHandler("Virtual")]
    public async Task<IActionResult> CreateVirtual(string? name, RemoteFormModel? remote, string? format = null, bool anonymousRead = true, CancellationToken cancellationToken = default)
        => Show(
            await Api<AdminRepositoriesApiController, RepositoriesViewModel>(api => api.CreateVirtual(
                new RepositoryCreateForm { Name = name, Remote = remote ?? new RemoteFormModel(), Format = format, AnonymousRead = anonymousRead },
                cancellationToken)),
            "Index");

    /// <summary>Creates a remote repository from the posted upstream settings, after the administrator's password.</summary>
    [Route("admin/repositories"), HttpPost, PageHandler("Remote")]
    public async Task<IActionResult> CreateRemote(string? name, RemoteFormModel? remote, string? password, string? anonymousRead, string? format = null, CancellationToken cancellationToken = default)
        => Show(
            await Api<AdminRepositoriesApiController, RepositoriesViewModel>(api => api.CreateRemote(
                new RepositoryCreateForm
                {
                    Name = name,
                    Remote = remote ?? new RemoteFormModel(),
                    Format = format,
                    Password = password,
                    // Absent = the default; anything but "true" is "no".
                    AnonymousRead = string.IsNullOrEmpty(anonymousRead) ? null : string.Equals(anonymousRead, "true", StringComparison.OrdinalIgnoreCase),
                },
                cancellationToken)),
            "Index");

    // The format module's create fields, posted as field:{name}.
    private Dictionary<string, string?> PostedFields()
        => !Request.HasFormContentType
            ? new Dictionary<string, string?>()
            : Request.Form.Keys
                .Where(k => k.StartsWith("field:", StringComparison.Ordinal))
                .ToDictionary(k => k.Substring("field:".Length), k => (string?)Request.Form[k].ToString(), StringComparer.Ordinal);

    // ---------------------------------------------------------------- RepositoryEdit

    /// <summary>Shows one repository.</summary>
    [Route("admin/repositories/{name}"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Edit(string name, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoryEditViewModel>(api => api.GetRepository(name, cancellationToken)), "Edit");

    /// <summary>Saves the repository.</summary>
    [Route("admin/repositories/{name}"), HttpPost, PageHandler]
    public async Task<IActionResult> Save(string name, RepositoryEditForm? form, string? password, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoryEditViewModel>(api => api.Save(name, Body(form, password), cancellationToken)), "Edit");

    /// <summary>Saves the policy of a local Maven repository.</summary>
    [Route("admin/repositories/{name}"), HttpPost, PageHandler("MavenSettings")]
    public async Task<IActionResult> MavenSettings(string name, RepositoryEditForm? form, string? policy, string? redeploy, string? snapshotBuilds, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoryEditViewModel>(api => api.MavenSettings(name, Body(form, null, policy: policy, redeploy: redeploy, snapshotBuilds: snapshotBuilds), cancellationToken)), "Edit");

    /// <summary>Asks a remote repository's upstream with the unsaved values and saves nothing.</summary>
    [Route("admin/repositories/{name}"), HttpPost, PageHandler("Test")]
    public async Task<IActionResult> Test(string name, RepositoryEditForm? form, string? password, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoryEditViewModel>(api => api.Test(name, Body(form, password), cancellationToken)), "Edit");

    /// <summary>Deletes everything a remote repository cached.</summary>
    [Route("admin/repositories/{name}"), HttpPost, PageHandler("Purge")]
    public async Task<IActionResult> Purge(string name, RepositoryEditForm? form, string? password, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoryEditViewModel>(api => api.Purge(name, Body(form, password), cancellationToken)), "Edit");

    /// <summary>Deletes one cached package of a remote repository.</summary>
    [Route("admin/repositories/{name}"), HttpPost, PageHandler("PurgePackage")]
    public async Task<IActionResult> PurgePackage(string name, RepositoryEditForm? form, string? password, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoryEditViewModel>(api => api.PurgePackage(name, Body(form, password), cancellationToken)), "Edit");

    /// <summary>Adds a member at the end of a virtual repository.</summary>
    [Route("admin/repositories/{name}"), HttpPost, PageHandler("AddMember")]
    public async Task<IActionResult> AddMember(string name, RepositoryEditForm? form, string? member, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoryEditViewModel>(api => api.AddMember(name, Body(form, null, member: member), cancellationToken)), "Edit");

    /// <summary>Removes a member of a virtual repository.</summary>
    [Route("admin/repositories/{name}"), HttpPost, PageHandler("RemoveMember")]
    public async Task<IActionResult> RemoveMember(string name, RepositoryEditForm? form, string? member, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoryEditViewModel>(api => api.RemoveMember(name, Body(form, null, member: member), cancellationToken)), "Edit");

    /// <summary>Moves a member of a virtual repository up or down.</summary>
    [Route("admin/repositories/{name}"), HttpPost, PageHandler("MoveMember")]
    public async Task<IActionResult> MoveMember(string name, RepositoryEditForm? form, string? member, string? direction, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoryEditViewModel>(api => api.MoveMember(name, Body(form, null, member: member, direction: direction), cancellationToken)), "Edit");

    /// <summary>Sets the member of a virtual repository that receives uploads.</summary>
    [Route("admin/repositories/{name}"), HttpPost, PageHandler("Deploy")]
    public async Task<IActionResult> Deploy(string name, RepositoryEditForm? form, string? deploy, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoryEditViewModel>(api => api.Deploy(name, Body(form, null, deploy: deploy), cancellationToken)), "Edit");

    /// <summary>Shows where a package version would come from in a virtual repository.</summary>
    [Route("admin/repositories/{name}"), HttpPost, PageHandler("Preview")]
    public async Task<IActionResult> Preview(string name, RepositoryEditForm? form, CancellationToken cancellationToken)
        => Show(await Api<AdminRepositoriesApiController, RepositoryEditViewModel>(api => api.Preview(name, Body(form, null), cancellationToken)), "Edit");

    // What the page's forms post, as the twin's body.
    private static RepositoryEditRequest Body(RepositoryEditForm? form, string? password, string? member = null, string? direction = null, string? deploy = null, string? policy = null, string? redeploy = null, string? snapshotBuilds = null)
        => new()
        {
            AnonymousRead = form?.AnonymousRead ?? false,
            Remote = form?.Remote ?? new RemoteFormModel(),
            PreviewPackageId = form?.PreviewPackageId,
            PreviewVersion = form?.PreviewVersion,
            PackageId = form?.PackageId,
            PackageVersion = form?.PackageVersion,
            Password = password,
            Member = member,
            Direction = direction,
            Deploy = deploy,
            Policy = policy,
            Redeploy = redeploy,
            SnapshotBuilds = snapshotBuilds,
        };

    // The screens print the reason from their model (Error), so a refusal is not copied into ModelState as well; the message of the last
    // redirect is moved into the model.
    private IActionResult Show<TModel>(UiApiResponse<TModel> response, string view)
        where TModel : class, IFlashMessage
        => Render(response, view, model => { Flash(model); return model; }, modelShowsFormErrors: true);

    // ---------------------------------------------------------------- RepositoryDelete

    /// <summary>Shows what deleting the repository does.</summary>
    [Route("admin/repositories/{name}/delete"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> DeleteRepository(string name, CancellationToken cancellationToken)
        => Render(await Api<AdminRepositoriesApiController, RepositoryDeleteViewModel>(api => api.GetDeletion(name, cancellationToken)), "Delete", modelShowsFormErrors: true);

    /// <summary>Deletes the repository, then returns to the list.</summary>
    [Route("admin/repositories/{name}/delete"), HttpPost, PageHandler]
    public async Task<IActionResult> DeleteRepositoryConfirmed(string name, CancellationToken cancellationToken)
        => Render(await Api<AdminRepositoriesApiController, RepositoryDeleteViewModel>(api => api.DeleteRepository(name, cancellationToken)), "Delete", modelShowsFormErrors: true);
}
