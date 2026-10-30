using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>
/// Copies every row of the running relational database into another relational database, then saves the
/// new <c>Database</c> settings. One copy at a time; <see cref="DatabaseCopyWorker"/> runs the queued request.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The <see cref="MaintenanceGate"/> closes when the request is accepted (<see cref="TryQueue"/>).</item>
/// <item>Checks that need no access to the target run first, so a refusal leaves it as it was: the source has no
/// pending migrations, and nothing in it is something the target cannot hold (<see cref="DatabaseCopyPreflight"/>,
/// judged on the target's model). Then the target: reachable (a database that does not exist yet is created), its
/// schema migrated, every mapped table empty (the <c>default</c> repository row of the <c>AddRepositories</c> migration and the built-in groups,
/// targets and grants of <c>AddAccessControl</c> do not count; they are removed, the source's own rows replacing them). A target that is current and not empty is refused untouched; one
/// that needed migrating and is then found not empty keeps its updated schema, and the refusal says so.</item>
/// <item>Tables in foreign-key order, in batches of <see cref="BatchSize"/> rows ordered by primary key, with
/// the provider's explicit-key hooks (<see cref="IContext.BeginKeyedInsertAsync"/>).</item>
/// <item>Row counts and a checksum of the keys must match; only then the settings are saved and the gate stays
/// closed until the restart.</item>
/// <item>On a failure before the settings are written (including a cancellation before the file is replaced) the rows
/// copied are deleted from the target (never from the source), the gate reopens and no setting is saved. From the
/// moment the settings are being written the copy is never undone: a failure then (<c>FailAfterSaveAsync</c>) keeps the
/// verified target, leaves the gate closed until the restart and reports that the settings may need checking.</item>
/// </list>
/// Connection strings are never logged or put in the status; error text is scrubbed of them.
/// </remarks>
public class DatabaseCopyService
{
    /// <summary>Rows read, inserted and saved at a time.</summary>
    public const int BatchSize = 1000;

    /// <summary>The reason the gate shows while a copy runs.</summary>
    public const string GateReason = "Copying the database";

    /// <summary>Why a copy is refused on a node of a cluster (<c>Ha:Enabled</c>).</summary>
    public const string ClusterRefusal = "The database cannot be copied while this node is part of a cluster (Ha:Enabled): the other nodes cannot be made read-only reliably. Stop the other nodes, turn Ha:Enabled off, copy, then re-enable it.";

    private static readonly string[] SecretKeys = { "password", "pwd", "user id", "uid", "user", "username", "accountkey", "sharedaccesssignature" };

    private readonly IServiceScopeFactory _scopes;
    private readonly IReadOnlyList<IDatabaseContextFactory> _factories;
    private readonly MaintenanceGate _gate;
    private readonly SettingsService _settings;
    private readonly IOptionsMonitor<DatabaseOptions> _current;
    private readonly ILogger<DatabaseCopyService> _logger;
    private readonly ClusterRuntime? _cluster;
    private readonly Channel<DatabaseCopyRequest> _queue = Channel.CreateBounded<DatabaseCopyRequest>(1);
    private readonly object _lock = new();
    private DatabaseCopyStatus _status = DatabaseCopyStatus.Idle;

    public DatabaseCopyService(
        IServiceScopeFactory scopes,
        IEnumerable<IDatabaseContextFactory> factories,
        MaintenanceGate gate,
        SettingsService settings,
        IOptionsMonitor<DatabaseOptions> current,
        ILogger<DatabaseCopyService> logger,
        ClusterRuntime? cluster = null)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _factories = (factories ?? throw new ArgumentNullException(nameof(factories))).ToList();
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cluster = cluster;
    }

    /// <summary>The latest progress. In a cluster, the progress of a copy running on another node when this node has none of its own.</summary>
    public DatabaseCopyStatus Status
    {
        get
        {
            lock (_lock) return _status != DatabaseCopyStatus.Idle ? _status : (RemoteStatus ?? _status);
        }
    }

    /// <summary>This node's own progress (what the cluster sync publishes).</summary>
    internal DatabaseCopyStatus LocalStatus { get { lock (_lock) return _status; } }

    /// <summary>The progress another node published (set by <see cref="ClusterSyncService"/>), or null.</summary>
    internal DatabaseCopyStatus? RemoteStatus { get; set; }

    /// <summary>
    /// Test seam: runs after each batch is saved, with the table and the rows of it copied so far. An
    /// exception it throws fails the copy exactly as a failing insert would.
    /// </summary>
    internal Func<string, long, CancellationToken, Task>? AfterBatch { get; set; }

    /// <summary>Whether a database of <paramref name="type"/> can be a copy's source or target (it is relational).</summary>
    public bool IsRelational(string? type) => FactoryFor(type) != null;

    /// <summary>The running database's type, if it is relational (a copy can start from it); else null.</summary>
    public string? CurrentRelationalType => FactoryFor(_current.CurrentValue.Type)?.DatabaseType;

    /// <summary>The <c>Database:Type</c> values a copy can use, for the Settings page.</summary>
    public IReadOnlyList<string> RelationalTypes => _factories.Select(f => f.DatabaseType).ToList();

    /// <summary>
    /// Whether <paramref name="type"/> and <paramref name="connectionString"/> name the database the application is running on.
    /// </summary>
    public bool IsCurrentDatabase(string? type, string? connectionString)
    {
        var current = _current.CurrentValue;
        return string.Equals(current.Type, type, StringComparison.OrdinalIgnoreCase)
               && string.Equals(current.ConnectionString, connectionString, StringComparison.Ordinal);
    }

    /// <summary>
    /// Accepts a copy: closes the gate and queues it for <see cref="DatabaseCopyWorker"/>.
    /// </summary>
    /// <param name="request">The copy.</param>
    /// <param name="refusal">Why it was refused, when it was.</param>
    /// <returns>False when another copy is active or the database is already read-only.</returns>
    public bool TryQueue(DatabaseCopyRequest request, out string? refusal)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        // The other nodes cannot be confirmed read-only before the copy reads, and after a success they would reopen
        // and write to the old database; so a copy is refused in a cluster.
        if (_cluster is { IsEnabled: true })
        {
            refusal = ClusterRefusal;
            return false;
        }

        lock (_lock)
        {
            if (_status.IsActive || RemoteStatus is { IsActive: true })
            {
                refusal = "Another database copy is already running.";
                return false;
            }

            if (_gate.IsClosed)
            {
                refusal = $"The database is read-only: {_gate.Reason}.";
                return false;
            }

            if (!_queue.Writer.TryWrite(request))
            {
                refusal = "Another database copy is already queued.";
                return false;
            }

            _gate.Close(GateReason);
            _status = DatabaseCopyStatus.Idle with { Phase = DatabaseCopyPhase.Queued, Message = "Waiting to start." };
        }

        refusal = null;
        return true;
    }

    /// <summary>Test seam: runs after the copy is verified, just before the settings are saved.</summary>
    internal Func<Task>? BeforeSettingsSaved { get; set; }

    /// <summary>Test seam: runs after the settings were saved, before the gate closes until restart.</summary>
    internal Func<Task>? AfterSettingsSaved { get; set; }

    /// <summary>The queued requests, for the worker.</summary>
    internal IAsyncEnumerable<DatabaseCopyRequest> DequeueAllAsync(CancellationToken cancellationToken)
        => _queue.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Runs one copy to the end and reports it through <see cref="Status"/>; never throws. The gate must be
    /// closed (<see cref="TryQueue"/>).
    /// </summary>
    public async Task RunAsync(DatabaseCopyRequest request, CancellationToken cancellationToken)
    {
        IContext? target = null;
        IReadOnlyList<DatabaseTable> tables = Array.Empty<DatabaseTable>();
        var touchedTarget = false;
        var pastPointOfNoReturn = false;
        IReadOnlyList<string> errors;

        // Costs nothing without a listener. Tags: engine names only, never the connection string.
        using var activity = BaGetDiagnostics.Source.StartActivity(BaGetDiagnostics.Spans.DatabaseCopy);
        activity?.SetTag(BaGetDiagnostics.Tags.TargetType, request.TargetType);

        try
        {
            Update(s => s with { Phase = DatabaseCopyPhase.Checking, Message = "Connecting to the target." });

            using var scope = _scopes.CreateScope();
            var source = AsDbContext(scope.ServiceProvider.GetRequiredService<IContext>());
            var sourceFactory = FactoryFor(_current.CurrentValue.Type)
                ?? throw new CopyRefusedException("The running database is not relational, so it cannot be copied.");
            activity?.SetTag(BaGetDiagnostics.Tags.SourceType, sourceFactory.DatabaseType);
            var factory = FactoryFor(request.TargetType)
                ?? throw new CopyRefusedException($"\"{request.TargetType}\" is not a relational database type, so it cannot be a copy's target.");
            if (IsCurrentDatabase(request.TargetType, request.TargetConnectionString))
            {
                throw new CopyRefusedException("The target is the database BaGet is running on.");
            }

            target = factory.Create(
                request.TargetConnectionString,
                new DatabaseOptions { MySqlServerVersion = request.MySqlServerVersion },
                builder => MaintenanceGate.AllowWrites(builder));
            var targetDb = AsDbContext(target);

            var pendingSource = (await source.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (pendingSource.Count > 0)
            {
                throw new CopyRefusedException(
                    $"The running database has {pendingSource.Count} pending migration(s) ({string.Join(", ", pendingSource)}); apply them (restart with RunMigrationsAtStartup) before copying.");
            }

            tables = DatabaseTable.InDependencyOrder(targetDb.Model);

            // Everything the target cannot hold is reported at once, before the target is created, migrated or changed.
            var preflight = new DatabaseCopyPreflight(request.TargetType, targetDb.GetService<IDesignTimeModel>().Model);
            foreach (var table in tables)
            {
                Update(s => s with { Table = table.Name, Message = $"Checking {table.Name}." });
                await ForEachBatchAsync(source, table, rows =>
                {
                    preflight.Inspect(table, rows.Select(r => r.Entity));
                    return Task.CompletedTask;
                }, cancellationToken).ConfigureAwait(false);
                preflight.EndTable();
            }

            if (preflight.Problems.Count > 0)
            {
                throw new CopyRefusedException(preflight.Problems.ToArray());
            }

            // The first access to the target. A database that does not exist yet (a new SQLite file, a SQL Server database the login may create) cannot be
            // connected to, but creating it is what BaGet's own migrations do; an unreachable one fails there.
            if (!await targetDb.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await target.RunMigrationsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    throw new CopyRefusedException(
                        "Cannot connect to the target database; it must be reachable with the given connection string (" + Describe(e, request) + ").");
                }
            }

            // A target that is already current and holds rows is refused before anything, even its schema, changes.
            var pendingTarget = await targetDb.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
            if (!pendingTarget.Any())
            {
                await EnsureEmptyAsync(targetDb, tables, schemaUpdated: false, cancellationToken).ConfigureAwait(false);
            }

            Update(s => s with { Phase = DatabaseCopyPhase.Migrating, Table = null, Message = "Preparing the target's schema." });
            await target.RunMigrationsAsync(cancellationToken).ConfigureAwait(false);
            await EnsureEmptyAsync(targetDb, tables, schemaUpdated: true, cancellationToken).ConfigureAwait(false);
            await RemoveSeededRowsAsync(targetDb, cancellationToken).ConfigureAwait(false);

            var totals = new Dictionary<DatabaseTable, long>();
            foreach (var table in tables) totals[table] = await table.CountAsync(source, cancellationToken).ConfigureAwait(false);

            Update(s => s with
            {
                Phase = DatabaseCopyPhase.Copying,
                TablesTotal = tables.Count,
                RowsTotal = totals.Values.Sum(),
                Message = "Copying rows.",
            });

            touchedTarget = true;
            var checksums = new Dictionary<DatabaseTable, string>();
            var done = 0;
            foreach (var table in tables)
            {
                checksums[table] = await CopyTableAsync(source, target, targetDb, table, totals[table], cancellationToken).ConfigureAwait(false);
                done++;
                var doneNow = done;
                Update(s => s with { TablesDone = doneNow });
            }

            Update(s => s with { Phase = DatabaseCopyPhase.Verifying, Table = null, Message = "Comparing the copy with the source." });
            await VerifyAsync(targetDb, tables, totals, checksums, cancellationToken).ConfigureAwait(false);

            Update(s => s with { Phase = DatabaseCopyPhase.SavingSettings, Message = "Saving the database settings." });
            if (BeforeSettingsSaved != null) await BeforeSettingsSaved().ConfigureAwait(false);

            // From here the settings may be on disk (and the next start use the target): the copy is never undone. Except
            // for a cancellation: the file is replaced by one synchronous rename after the last point that observes the token
            // (the save lock, the temporary file's write), so a cancelled save never replaced it.
            pastPointOfNoReturn = true;
            SettingsSaveResult saved;
            try
            {
                saved = await _settings.SaveAsync(request.SettingChanges, request.Actor, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                pastPointOfNoReturn = false;
                throw;
            }

            if (!saved.Succeeded)
            {
                // A refusal means nothing was written, so the copy can still be undone.
                pastPointOfNoReturn = false;
                throw new CopyRefusedException(saved.Errors.Select(e => "The settings were not saved: " + e).ToArray());
            }

            if (AfterSettingsSaved != null) await AfterSettingsSaved().ConfigureAwait(false);

            activity?.SetTag(BaGetDiagnostics.Tags.Result, "Succeeded");
            activity?.SetTag(BaGetDiagnostics.Tags.Tables, tables.Count);
            activity?.SetTag(BaGetDiagnostics.Tags.Rows, totals.Values.Sum());

            _gate.CloseUntilRestart("The database was copied; restart BaGet to use the new database");
            Update(s => s with
            {
                Phase = DatabaseCopyPhase.Succeeded,
                Table = null,
                Message = "The data was copied and the settings saved. Restart BaGet to use the new database.",
            });
            _logger.LogWarning(
                "{Actor} copied the {SourceType} database to a {TargetType} database ({Rows} rows in {Tables} tables)",
                request.Actor, sourceFactory.DatabaseType, request.TargetType, totals.Values.Sum(), tables.Count);

            try
            {
                await AsDbContext(target).DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Disposing the copy's target context failed after a successful copy");
            }

            return;
        }
        catch (CopyRefusedException e)
        {
            errors = e.Problems;
        }
        catch (Exception e)
        {
            errors = new[] { Describe(e, request) };
        }

        activity?.SetTag(BaGetDiagnostics.Tags.Result, "Failed");
        activity?.SetStatus(ActivityStatusCode.Error);

        if (pastPointOfNoReturn)
        {
            await FailAfterSaveAsync(request, target, errors).ConfigureAwait(false);
            return;
        }

        await FailAsync(request, target, tables, touchedTarget, errors).ConfigureAwait(false);
    }

    /// <summary>
    /// A failure once the settings were being saved: the target holds the verified copy and the saved settings (if any)
    /// point at it, so it is kept, the gate stays closed until the restart (writes to the source would be lost) and the
    /// error is reported.
    /// </summary>
    private async Task FailAfterSaveAsync(DatabaseCopyRequest request, IContext? target, IReadOnlyList<string> errors)
    {
        if (target != null)
        {
            try
            {
                await AsDbContext(target).DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Nothing left to clean up.
            }
        }

        _gate.CloseUntilRestart("The database was copied but the settings may not have been saved; check them and restart BaGet");
        _logger.LogError("Database copy to {TargetType} failed while saving the settings: {Errors}", request.TargetType, string.Join(" | ", errors));
        Update(s => s with
        {
            Phase = DatabaseCopyPhase.Failed,
            Table = null,
            Errors = errors,
            Message = "The data was copied and verified, but saving the settings failed. The target was kept; check the database settings, then restart BaGet.",
        });
    }

    private async Task FailAsync(
        DatabaseCopyRequest request,
        IContext? target,
        IReadOnlyList<DatabaseTable> tables,
        bool touchedTarget,
        IReadOnlyList<string> errors)
    {
        var all = errors.ToList();
        if (target != null)
        {
            if (touchedTarget)
            {
                Update(s => s with { Phase = DatabaseCopyPhase.RollingBack, Table = null, Message = "Removing the copied rows from the target." });
                try
                {
                    // Only ever the target, which was empty when the copy began.
                    var db = AsDbContext(target);
                    db.ChangeTracker.Clear();
                    foreach (var table in tables.Reverse())
                    {
                        await table.DeleteAllAsync(db, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    all.Add("The rows copied so far could not be removed from the target; empty it before trying again. " + Describe(e, request));
                }
            }

            try
            {
                await AsDbContext(target).DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Nothing left to clean up.
            }
        }

        _gate.Open();
        _logger.LogError("Database copy to {TargetType} failed: {Errors}", request.TargetType, string.Join(" | ", all));
        Update(s => s with
        {
            Phase = DatabaseCopyPhase.Failed,
            Table = null,
            Errors = all,
            Message = "The copy failed. The running database is unchanged and no setting was saved.",
        });
    }

    private async Task<string> CopyTableAsync(
        DbContext source,
        IContext target,
        DbContext targetDb,
        DatabaseTable table,
        long total,
        CancellationToken cancellationToken)
    {
        var dateProperties = table.EntityType.GetProperties()
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?))
            .ToList();
        var computed = table.EntityType.GetProperties()
            .Where(p => !p.IsKey() && p.ValueGenerated == ValueGenerated.OnAddOrUpdate)
            .ToList();

        long copied = 0;
        using var checksum = new DatabaseTable.Checksum();
        using var activity = BaGetDiagnostics.Source.StartActivity(BaGetDiagnostics.Spans.DatabaseCopyTable);
        activity?.SetTag(BaGetDiagnostics.Tags.Table, table.Name);
        activity?.SetTag(BaGetDiagnostics.Tags.Rows, total);

        Update(s => s with { Table = table.Name, TableRowsCopied = 0, TableRowsTotal = total, Message = $"Copying {table.Name}." });
        var rowsBefore = Status.RowsCopied;

        await target.BeginKeyedInsertAsync(table.EntityType, cancellationToken).ConfigureAwait(false);
        try
        {
            await ForEachBatchAsync(source, table, async rows =>
            {
                foreach (var row in rows)
                {
                    checksum.Add(table.KeyText(row.Entity));

                    var entry = targetDb.Add(row.Entity);
                    foreach (var shadow in row.Shadow) entry.Property(shadow.Key).CurrentValue = shadow.Value;

                    // SQLite, SQL Server and MySQL return dates of unspecified kind; BaGet stores UTC, and PostgreSQL accepts
                    // nothing else. EF compares DateTimes without their kind, so assigning the same instant with another kind
                    // is ignored as "no change": move off it first. The entry reads the mapped field, so a property whose
                    // getter already forces UTC (Package.Published) is fixed too.
                    foreach (var property in dateProperties)
                    {
                        var member = entry.Property(property.Name);
                        if (member.CurrentValue is DateTime { Kind: DateTimeKind.Unspecified } date)
                        {
                            member.CurrentValue = DateTime.MinValue;
                            member.CurrentValue = DateTime.SpecifyKind(date, DateTimeKind.Utc);
                        }
                    }

                    // Values the database generates itself (row versions) are not inserted.
                    foreach (var property in computed)
                    {
                        entry.Property(property.Name).CurrentValue = property.ClrType.IsValueType && Nullable.GetUnderlyingType(property.ClrType) == null
                            ? Activator.CreateInstance(property.ClrType)
                            : null;
                    }
                }

                try
                {
                    await targetDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException e)
                {
                    throw new CopyRefusedException(
                        $"Inserting into {table.Name} failed (rows {table.KeyText(rows[0].Entity)} to {table.KeyText(rows[rows.Count - 1].Entity)}): {e.GetBaseException().Message}");
                }

                targetDb.ChangeTracker.Clear();
                copied += rows.Count;
                var copiedNow = copied;
                Update(s => s with { TableRowsCopied = copiedNow, RowsCopied = rowsBefore + copiedNow });

                if (AfterBatch != null) await AfterBatch(table.Name, copiedNow, cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            activity?.SetTag(BaGetDiagnostics.Tags.Result, "Failed");
            activity?.SetStatus(ActivityStatusCode.Error);

            try
            {
                await target.EndKeyedInsertAsync(table.EntityType, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The original failure is the one to report.
            }

            throw;
        }

        await target.EndKeyedInsertAsync(table.EntityType, cancellationToken).ConfigureAwait(false);
        activity?.SetTag(BaGetDiagnostics.Tags.Result, "Succeeded");
        return checksum.Finish();
    }

    private static async Task VerifyAsync(
        DbContext target,
        IReadOnlyList<DatabaseTable> tables,
        IReadOnlyDictionary<DatabaseTable, long> totals,
        IReadOnlyDictionary<DatabaseTable, string> checksums,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        foreach (var table in tables)
        {
            var count = await table.CountAsync(target, cancellationToken).ConfigureAwait(false);
            if (count != totals[table])
            {
                problems.Add($"{table.Name}: the source has {totals[table]} rows, the target {count}.");
                continue;
            }

            using var checksum = new DatabaseTable.Checksum();
            await ForEachBatchAsync(target, table, rows =>
            {
                foreach (var row in rows) checksum.Add(table.KeyText(row.Entity));
                return Task.CompletedTask;
            }, cancellationToken).ConfigureAwait(false);

            if (checksum.Finish() != checksums[table])
            {
                problems.Add($"{table.Name}: the keys of the target differ from the source's.");
            }
        }

        if (problems.Count > 0) throw new CopyRefusedException(problems.ToArray());
    }

    // The rows the AddRepositories and AddAccessControl migrations create: what a fresh target holds, so not "data".
    // The source's own copies of them (edited or not) replace them.
    private static readonly System.Linq.Expressions.Expression<Func<Repository, bool>> SeededDefault
        = r => r.Key == Repository.DefaultKey && r.Name == Repository.DefaultName;

    // The grants of the seeded targets go with their target (cascade); the built-in groups' members do too.
    private static async Task RemoveSeededRowsAsync(DbContext target, CancellationToken cancellationToken)
    {
        await target.Set<PermissionTarget>().Where(t => t.IsBuiltIn).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await target.Set<Group>().Where(g => g.IsBuiltIn).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await target.Set<Repository>().Where(SeededDefault).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    // How many rows of a table the migrations seeded (they are not data), or 0 for a table nothing seeds.
    private static async Task<int> SeededCountAsync(DbContext target, Type clrType, CancellationToken cancellationToken)
    {
        if (clrType == typeof(Repository)) return await target.Set<Repository>().CountAsync(SeededDefault, cancellationToken).ConfigureAwait(false);
        if (clrType == typeof(Group)) return await target.Set<Group>().CountAsync(g => g.IsBuiltIn, cancellationToken).ConfigureAwait(false);
        if (clrType == typeof(PermissionTarget)) return await target.Set<PermissionTarget>().CountAsync(t => t.IsBuiltIn, cancellationToken).ConfigureAwait(false);
        if (clrType == typeof(PermissionGrant))
        {
            return await target.Set<PermissionGrant>()
                .CountAsync(g => target.Set<PermissionTarget>().Any(t => t.Key == g.TargetKey && t.IsBuiltIn), cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task EnsureEmptyAsync(
        DbContext target,
        IReadOnlyList<DatabaseTable> tables,
        bool schemaUpdated,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        foreach (var table in tables)
        {
            var count = await table.CountAsync(target, cancellationToken).ConfigureAwait(false)
                - await SeededCountAsync(target, table.EntityType.ClrType, cancellationToken).ConfigureAwait(false);
            if (count > 0)
            {
                problems.Add(schemaUpdated
                    ? $"The target is not empty: {table.Name} has {count} row(s). The target's schema was updated, but nothing was copied."
                    : $"The target is not empty: {table.Name} has {count} row(s). Nothing was changed.");
            }
        }

        if (problems.Count > 0) throw new CopyRefusedException(problems.ToArray());
    }

    private static async Task ForEachBatchAsync(
        DbContext context,
        DatabaseTable table,
        Func<List<CopyRow>, Task> action,
        CancellationToken cancellationToken)
    {
        var skip = 0;
        while (true)
        {
            var rows = await table.ReadBatchAsync(context, skip, BatchSize, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0) return;

            await action(rows).ConfigureAwait(false);
            skip += rows.Count;
            if (rows.Count < BatchSize) return;
        }
    }

    private void Update(Func<DatabaseCopyStatus, DatabaseCopyStatus> change)
    {
        lock (_lock) _status = change(_status);
    }

    private IDatabaseContextFactory? FactoryFor(string? type)
        => string.IsNullOrEmpty(type)
            ? null
            : _factories.FirstOrDefault(f => string.Equals(f.DatabaseType, type, StringComparison.OrdinalIgnoreCase));

    private static DbContext AsDbContext(IContext context)
        => context as DbContext ?? throw new InvalidOperationException("The database context is not an Entity Framework context.");

    /// <summary>An error as text safe to show: no connection string, no credential from it.</summary>
    internal static string Describe(Exception exception, DatabaseCopyRequest request)
        => Scrub(exception.GetBaseException().Message, request.TargetConnectionString);

    /// <summary>Removes the connection string, and the credentials inside it, from <paramref name="message"/>.</summary>
    internal static string Scrub(string message, string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return message;

        message = message.Replace(connectionString, "[connection string]", StringComparison.Ordinal);
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;

            var key = part.Substring(0, separator).Trim().ToLowerInvariant();
            var value = part.Substring(separator + 1).Trim().Trim('\'', '"');
            if (value.Length > 0 && SecretKeys.Contains(key))
            {
                message = message.Replace(value, "[hidden]", StringComparison.Ordinal);
            }
        }

        return message;
    }

    /// <summary>A failure that is the user's to fix: shown as is (it holds no connection string).</summary>
    private sealed class CopyRefusedException : Exception
    {
        public CopyRefusedException(string problem) : this(new[] { problem }) { }

        public CopyRefusedException(string[] problems) : base(string.Join(" ", problems)) => Problems = problems;

        public string[] Problems { get; }
    }
}
