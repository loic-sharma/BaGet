using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using BaGet.Identity.Application.Models.Policies;

namespace BaGet.Identity.Application.UseCases.Policies;

/// <summary>
/// The retention screens: the policies and the form that creates one, one policy (save, dry run, run now, delete) and the legal holds.
/// Creating, saving, running and deleting a policy and lifting a hold ask for the administrator's password; a dry run and placing a hold
/// do not. A refusal of the administration (<see cref="AccessModelException"/>) is shown on the screen the action comes back to. Every
/// write is refused while the server is read-only.
/// </summary>
public sealed class RetentionUseCase : PolicyUseCaseBase
{
    private static readonly ScreenRef PoliciesScreen = ScreenCatalog.AdminRetention;
    private static readonly ScreenRef PolicyScreen = ScreenCatalog.AdminRetentionPolicy;
    private static readonly ScreenRef HoldsScreen = ScreenCatalog.AdminRetentionHolds;

    private readonly IRetentionAdministration _retention;
    private readonly RetentionJobs _jobs;
    private readonly IRepositoryService _repositories;
    private readonly IAccessAdministration _access;

    /// <summary>Creates the use case.</summary>
    public RetentionUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IRetentionAdministration retention,
        RetentionJobs jobs,
        IRepositoryService repositories,
        IAccessAdministration access,
        MaintenanceGate? gate = null)
        : base(users, confirmation, gate)
    {
        _retention = retention ?? throw new ArgumentNullException(nameof(retention));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _access = access ?? throw new ArgumentNullException(nameof(access));
    }

    // ------------------------------------------------------------------ policies

    /// <summary>Lists the policies. <paramref name="returnPage"/> is the address to come back to after signing in again.</summary>
    public async Task<ScreenResult<RetentionViewModel>> GetPoliciesAsync(ClaimsPrincipal caller, string? returnPage, CancellationToken cancellationToken)
    {
        var model = new RetentionViewModel();

        return ScreenResult<RetentionViewModel>.Ok(await ShowPoliciesAsync(model, caller, returnPage, cancellationToken));
    }

    /// <summary>Creates a policy and opens it.</summary>
    public async Task<ScreenResult<RetentionViewModel>> CreateAsync(ClaimsPrincipal caller, RetentionPolicyBody? body, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = new RetentionViewModel { Form = body?.Form ?? new RetentionPolicyForm() };
        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<RetentionViewModel>.SignIn();

        if (!await Confirmation.IsConfirmedAsync(current, body?.Password))
        {
            model.Error = NotConfirmed;

            return ScreenResult<RetentionViewModel>.Invalid(await ShowPoliciesAsync(model, caller, returnPage, cancellationToken), NotConfirmed);
        }

        try
        {
            var policy = await _retention.SavePolicyAsync(model.Form.ToInput(0), ActorOf(caller), cancellationToken);

            return Back<RetentionViewModel>(PolicyScreen, Route(("key", policy.Key)), $"Policy {policy.Name} was created.", null);
        }
        catch (AccessModelException e)
        {
            model.Error = e.Message;

            return ScreenResult<RetentionViewModel>.Invalid(await ShowPoliciesAsync(model, caller, returnPage, cancellationToken), e.Message);
        }
    }

    private async Task<RetentionViewModel> ShowPoliciesAsync(RetentionViewModel model, ClaimsPrincipal caller, string? returnPage, CancellationToken cancellationToken)
    {
        model.Policies = await _retention.ListPoliciesAsync(cancellationToken);
        model.Repositories = await PolicyScreenSupport.RetentionRepositoriesAsync(_repositories, cancellationToken);
        model.ProjectNames = await PolicyScreenSupport.ProjectNamesAsync(_access, cancellationToken);
        await Confirmation.LoadAsync(model, caller, returnPage);

        return model;
    }

    // ------------------------------------------------------------------ one policy

    /// <summary>Shows the policy.</summary>
    public async Task<ScreenResult<RetentionPolicyViewModel>> GetPolicyAsync(ClaimsPrincipal caller, int key, string? returnPage, CancellationToken cancellationToken)
    {
        var model = new RetentionPolicyViewModel { Key = key };
        if (!await LoadPolicyAsync(model, cancellationToken)) return ScreenResult<RetentionPolicyViewModel>.NotFound();

        model.Form = RetentionPolicyForm.From(model.View!);
        await Confirmation.LoadAsync(model, caller, returnPage);

        return ScreenResult<RetentionPolicyViewModel>.Ok(model);
    }

    /// <summary>Saves the rules.</summary>
    public Task<ScreenResult<RetentionPolicyViewModel>> SaveAsync(ClaimsPrincipal caller, int key, RetentionPolicyBody? body, CancellationToken cancellationToken)
        => ActAsync<RetentionPolicyViewModel>(caller, body?.Password, confirm: true, async () =>
        {
            var policy = await _retention.SavePolicyAsync((body?.Form ?? new RetentionPolicyForm()).ToInput(key), ActorOf(caller), cancellationToken);

            return $"Policy {policy.Name} was saved.";
        }, PolicyScreen, Route(("key", key)));

    /// <summary>Starts a dry run: the report of what a run would delete.</summary>
    public async Task<ScreenResult<RetentionPolicyViewModel>> DryRunAsync(ClaimsPrincipal caller, int key, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        if (await _retention.GetPolicyAsync(key, cancellationToken) == null) return ScreenResult<RetentionPolicyViewModel>.NotFound();

        return _jobs.TryStart(key, dryRun: true, ActorOf(caller), out var refusal)
            ? Back<RetentionPolicyViewModel>(PolicyScreen, Route(("key", key)), "The dry run started; nothing is deleted.", null)
            : Back<RetentionPolicyViewModel>(PolicyScreen, Route(("key", key)), null, refusal);
    }

    /// <summary>Starts a run that deletes.</summary>
    public async Task<ScreenResult<RetentionPolicyViewModel>> RunAsync(ClaimsPrincipal caller, int key, string? password, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        var view = await _retention.GetPolicyAsync(key, cancellationToken);
        if (view == null) return ScreenResult<RetentionPolicyViewModel>.NotFound();

        return await ActAsync<RetentionPolicyViewModel>(caller, password, confirm: true, () =>
        {
            if (!view.Policy.Enabled) throw new RetentionException("The policy is disabled: enable it first (a dry run works either way).");
            if (!_jobs.TryStart(key, dryRun: false, ActorOf(caller), out var refusal)) throw new RetentionException(refusal ?? "The run was not started.");

            return Task.FromResult($"{view.Policy.Name} started; versions it selects are deleted.");
        }, PolicyScreen, Route(("key", key)));
    }

    /// <summary>Deletes the policy (what it deleted stays deleted).</summary>
    public async Task<ScreenResult<RetentionPolicyViewModel>> DeleteAsync(ClaimsPrincipal caller, int key, string? password, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        if (await _retention.GetPolicyAsync(key, cancellationToken) == null) return ScreenResult<RetentionPolicyViewModel>.NotFound();

        return await ActAsync<RetentionPolicyViewModel>(caller, password, confirm: true, async () =>
        {
            await _retention.DeletePolicyAsync(key, ActorOf(caller), cancellationToken);

            return "The policy was deleted.";
        }, PoliciesScreen, null);
    }

    private async Task<bool> LoadPolicyAsync(RetentionPolicyViewModel model, CancellationToken cancellationToken)
    {
        model.View = await _retention.GetPolicyAsync(model.Key, cancellationToken);
        if (model.View == null) return false;

        model.Status = _jobs.Get(model.Key);
        model.Repositories = await PolicyScreenSupport.RetentionRepositoriesAsync(_repositories, cancellationToken);
        model.ProjectNames = await PolicyScreenSupport.ProjectNamesAsync(_access, cancellationToken);

        return true;
    }

    // ------------------------------------------------------------------ holds

    /// <summary>Lists the holds (of <paramref name="repository"/> only, when given).</summary>
    public async Task<ScreenResult<RetentionHoldsViewModel>> GetHoldsAsync(ClaimsPrincipal caller, string? repository, string? returnPage, CancellationToken cancellationToken)
    {
        var model = new RetentionHoldsViewModel { Repository = repository };

        return ScreenResult<RetentionHoldsViewModel>.Ok(await ShowHoldsAsync(model, caller, returnPage, cancellationToken));
    }

    /// <summary>Places a hold.</summary>
    public async Task<ScreenResult<RetentionHoldsViewModel>> AddHoldAsync(
        ClaimsPrincipal caller, string? repository, RetentionHoldForm? form, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = new RetentionHoldsViewModel
        {
            Repository = repository,
            HoldRepository = form?.HoldRepository,
            HoldName = form?.HoldName,
            HoldVersion = form?.HoldVersion,
            Reason = form?.Reason,
        };

        try
        {
            var hold = await _retention.AddHoldAsync(
                model.HoldRepository ?? string.Empty, model.HoldName ?? string.Empty, model.HoldVersion, model.Reason, ActorOf(caller), cancellationToken);

            return Back<RetentionHoldsViewModel>(
                HoldsScreen,
                Route(("repository", repository)),
                $"{hold.Name}{(string.IsNullOrEmpty(hold.Version) ? string.Empty : " " + hold.Version)} is on legal hold.",
                null);
        }
        catch (AccessModelException e)
        {
            model.Error = e.Message;

            return ScreenResult<RetentionHoldsViewModel>.Invalid(await ShowHoldsAsync(model, caller, returnPage, cancellationToken), e.Message);
        }
    }

    /// <summary>Lifts a hold.</summary>
    public Task<ScreenResult<RetentionHoldsViewModel>> RemoveHoldAsync(ClaimsPrincipal caller, string? repository, RetentionHoldRemoval? removal, CancellationToken cancellationToken)
        => ActAsync<RetentionHoldsViewModel>(caller, removal?.Password, confirm: true, async () =>
        {
            return await _retention.RemoveHoldAsync(removal?.Key ?? 0, ActorOf(caller), cancellationToken) ? "The hold was lifted." : "That hold no longer exists.";
        }, HoldsScreen, Route(("repository", repository)));

    private async Task<RetentionHoldsViewModel> ShowHoldsAsync(RetentionHoldsViewModel model, ClaimsPrincipal caller, string? returnPage, CancellationToken cancellationToken)
    {
        model.Holds = await _retention.ListHoldsAsync(model.Repository, cancellationToken);
        model.Repositories = await PolicyScreenSupport.RetentionRepositoriesAsync(_repositories, cancellationToken);
        await Confirmation.LoadAsync(model, caller, returnPage);

        return model;
    }
}
