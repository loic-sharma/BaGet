using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BaGet.Core;

/// <summary>
/// Refuses <c>SaveChanges</c> with a <see cref="MaintenanceException"/> while the
/// <see cref="MaintenanceGate"/> is closed, except on a context built with
/// <see cref="MaintenanceGate.AllowWrites(DbContextOptionsBuilder)"/>. Every database write in
/// BaGet goes through EF <c>SaveChanges</c> (there is no raw SQL outside migrations), so this is
/// the one place that makes the database read-only.
/// </summary>
public sealed class MaintenanceSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly MaintenanceGate _gate;

    public MaintenanceSaveChangesInterceptor(MaintenanceGate gate)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Check(eventData);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    private void Check(DbContextEventData eventData)
    {
        if (!_gate.IsClosed) return;
        if (MaintenanceGate.AreWritesAllowed(eventData.Context)) return;

        _gate.ThrowIfClosed();
    }
}
