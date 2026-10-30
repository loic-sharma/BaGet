using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using BaGet.Identity.Application.Models.Policies;

namespace BaGet.Identity.Application.UseCases.Policies;

/// <summary>
/// The replication screens: the replications and the form that creates one, and one replication (save, test the connection, run, resume, stop,
/// delete, its log). Everything but stopping asks for the administrator's password. Every write is refused while the server is read-only.
/// The peer's API key is write only: no model carries it, whatever the request sent (<see cref="ReplicationForm.WithoutSecret"/>), and it is never
/// part of a configuration's view.
/// </summary>
public sealed class ReplicationUseCase : PolicyUseCaseBase
{
    private static readonly ScreenRef ReplicationsScreen = ScreenCatalog.AdminReplication;
    private static readonly ScreenRef ConfigScreen = ScreenCatalog.AdminReplicationConfig;

    private readonly IReplicationAdministration _replication;
    private readonly ReplicationJobs _jobs;
    private readonly IRepositoryService _repositories;
    private readonly IReplicationInventory _inventory;

    /// <summary>Creates the use case.</summary>
    public ReplicationUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IReplicationAdministration replication,
        ReplicationJobs jobs,
        IRepositoryService repositories,
        IReplicationInventory inventory,
        MaintenanceGate? gate = null)
        : base(users, confirmation, gate)
    {
        _replication = replication ?? throw new ArgumentNullException(nameof(replication));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    // ------------------------------------------------------------------ replications

    /// <summary>Lists the replications. <paramref name="returnPage"/> is the address to come back to after signing in again.</summary>
    public async Task<ScreenResult<ReplicationViewModel>> GetReplicationsAsync(ClaimsPrincipal caller, string? returnPage, CancellationToken cancellationToken)
        => ScreenResult<ReplicationViewModel>.Ok(await ShowReplicationsAsync(new ReplicationViewModel(), caller, returnPage, cancellationToken));

    /// <summary>Creates a replication and opens it.</summary>
    public async Task<ScreenResult<ReplicationViewModel>> CreateAsync(ClaimsPrincipal caller, ReplicationBody? body, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var form = body?.Form ?? new ReplicationForm();
        var model = new ReplicationViewModel { Form = form.WithoutSecret() };
        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<ReplicationViewModel>.SignIn();

        if (!await Confirmation.IsConfirmedAsync(current, body?.Password))
        {
            model.Error = NotConfirmed;

            return ScreenResult<ReplicationViewModel>.Invalid(await ShowReplicationsAsync(model, caller, returnPage, cancellationToken), NotConfirmed);
        }

        try
        {
            var config = await _replication.SaveAsync(form.ToInput(0), ActorOf(caller), cancellationToken);

            return Back<ReplicationViewModel>(ConfigScreen, Route(("key", config.Key)), $"Replication {config.Name} was created.", null);
        }
        catch (AccessModelException e)
        {
            model.Error = e.Message;

            return ScreenResult<ReplicationViewModel>.Invalid(await ShowReplicationsAsync(model, caller, returnPage, cancellationToken), e.Message);
        }
    }

    private async Task<ReplicationViewModel> ShowReplicationsAsync(ReplicationViewModel model, ClaimsPrincipal caller, string? returnPage, CancellationToken cancellationToken)
    {
        model.Replications = await _replication.ListAsync(cancellationToken);
        model.Repositories = await PolicyScreenSupport.ReplicationRepositoriesAsync(_repositories, cancellationToken);
        await Confirmation.LoadAsync(model, caller, returnPage);

        return model;
    }

    // ------------------------------------------------------------------ one replication

    /// <summary>Shows the configuration.</summary>
    public async Task<ScreenResult<ReplicationConfigViewModel>> GetConfigAsync(ClaimsPrincipal caller, int key, string? returnPage, CancellationToken cancellationToken)
    {
        var model = new ReplicationConfigViewModel { Key = key };
        if (!await LoadConfigAsync(model, cancellationToken)) return ScreenResult<ReplicationConfigViewModel>.NotFound();

        model.Form = ReplicationForm.From(model.View!);
        await Confirmation.LoadAsync(model, caller, returnPage);

        return ScreenResult<ReplicationConfigViewModel>.Ok(model);
    }

    /// <summary>Saves the settings.</summary>
    public Task<ScreenResult<ReplicationConfigViewModel>> SaveAsync(ClaimsPrincipal caller, int key, ReplicationBody? body, CancellationToken cancellationToken)
        => ActAsync<ReplicationConfigViewModel>(caller, body?.Password, confirm: true, async () =>
        {
            var config = await _replication.SaveAsync((body?.Form ?? new ReplicationForm()).ToInput(key), ActorOf(caller), cancellationToken);

            return $"Replication {config.Name} was saved.";
        }, ConfigScreen, Route(("key", key)));

    /// <summary>Tests the connection with the typed settings; saves nothing.</summary>
    public async Task<ScreenResult<ReplicationConfigViewModel>> TestAsync(
        ClaimsPrincipal caller, int key, ReplicationBody? body, string? returnPage, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var form = body?.Form ?? new ReplicationForm();
        var model = new ReplicationConfigViewModel { Key = key, Form = form.WithoutSecret() };
        if (!await LoadConfigAsync(model, cancellationToken)) return ScreenResult<ReplicationConfigViewModel>.NotFound();

        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<ReplicationConfigViewModel>.SignIn();

        if (!await Confirmation.IsConfirmedAsync(current, body?.Password))
        {
            const string refusal = "The confirmation is incorrect, so nothing was tested.";
            model.Error = refusal;
            await Confirmation.LoadAsync(model, caller, returnPage);

            return ScreenResult<ReplicationConfigViewModel>.Invalid(model, refusal);
        }

        model.Test = await _replication.TestAsync(form.ToInput(key), cancellationToken);
        await Confirmation.LoadAsync(model, caller, returnPage);

        return ScreenResult<ReplicationConfigViewModel>.Ok(model);
    }

    /// <summary>Starts a run.</summary>
    public async Task<ScreenResult<ReplicationConfigViewModel>> RunAsync(ClaimsPrincipal caller, int key, string? password, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        var view = await _replication.GetAsync(key, cancellationToken);
        if (view == null) return ScreenResult<ReplicationConfigViewModel>.NotFound();

        return await ActAsync<ReplicationConfigViewModel>(caller, password, confirm: true, () => Start(caller, key, view.Config, "manual", null), ConfigScreen, Route(("key", key)));
    }

    /// <summary>Starts a run that continues the last interrupted or failed one.</summary>
    public async Task<ScreenResult<ReplicationConfigViewModel>> ResumeAsync(ClaimsPrincipal caller, int key, string? password, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        var view = await _replication.GetAsync(key, cancellationToken);
        if (view == null) return ScreenResult<ReplicationConfigViewModel>.NotFound();

        var runs = await _replication.ListRunsAsync(key, 1, cancellationToken);
        var last = runs.FirstOrDefault();
        var resume = last != null && last.Status is ReplicationRunStatus.Failed or ReplicationRunStatus.Interrupted ? last.Key : (int?)null;

        return await ActAsync<ReplicationConfigViewModel>(caller, password, confirm: true, () => Start(caller, key, view.Config, "resume", resume), ConfigScreen, Route(("key", key)));
    }

    private Task<string> Start(ClaimsPrincipal caller, int key, ReplicationConfig config, string trigger, int? resumedFrom)
    {
        if (!config.Enabled) throw new ReplicationException($"The replication '{config.Name}' is disabled: enable it first.");
        if (!_jobs.TryStart(key, trigger, ActorOf(caller), resumedFrom, out var refusal)) throw new ReplicationException(refusal ?? "The run was not started.");

        return Task.FromResult("The replication started; the log below shows its progress.");
    }

    /// <summary>Asks the running replication to stop; it ends as interrupted and can be resumed.</summary>
    public async Task<ScreenResult<ReplicationConfigViewModel>> StopAsync(int key, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        if (await _replication.GetAsync(key, cancellationToken) == null) return ScreenResult<ReplicationConfigViewModel>.NotFound();

        return _jobs.Cancel(key)
            ? Back<ReplicationConfigViewModel>(ConfigScreen, Route(("key", key)), "The replication was asked to stop; it ends as interrupted.", null)
            : Back<ReplicationConfigViewModel>(ConfigScreen, Route(("key", key)), null, "No run of this replication is going on this server.");
    }

    /// <summary>Deletes the configuration with its log.</summary>
    public async Task<ScreenResult<ReplicationConfigViewModel>> DeleteAsync(ClaimsPrincipal caller, int key, string? password, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        if (await _replication.GetAsync(key, cancellationToken) == null) return ScreenResult<ReplicationConfigViewModel>.NotFound();

        return await ActAsync<ReplicationConfigViewModel>(caller, password, confirm: true, async () =>
        {
            await _replication.DeleteAsync(key, ActorOf(caller), cancellationToken);

            return "The replication was deleted. What it copied stays where it is.";
        }, ReplicationsScreen, null);
    }

    private async Task<bool> LoadConfigAsync(ReplicationConfigViewModel model, CancellationToken cancellationToken)
    {
        model.View = await _replication.GetAsync(model.Key, cancellationToken);
        if (model.View == null) return false;

        model.Runs = await _replication.ListRunsAsync(model.Key, 25, cancellationToken);
        model.Job = _jobs.Get(model.Key);
        model.Repositories = await PolicyScreenSupport.ReplicationRepositoriesAsync(_repositories, cancellationToken);
        if (model.View.Repository != null)
        {
            try
            {
                model.LocalItems = (await _inventory.GetAsync(model.View.Repository, summary: true, cancellationToken))?.Count;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                model.LocalItems = null;
            }
        }

        return true;
    }
}
