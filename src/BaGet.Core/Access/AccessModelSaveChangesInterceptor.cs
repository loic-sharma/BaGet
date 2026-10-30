using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BaGet.Core;

/// <summary>
/// Bumps <see cref="AccessModelVersion"/> after any committed change to what access decisions read: the access model's
/// own tables, repositories (the anonymous-read flag, the project), projects and organization membership. Doing it
/// here, once, means no code path that edits those rows can forget to invalidate the evaluator's cache.
/// </summary>
public sealed class AccessModelSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly Type[] Relevant =
    {
        typeof(Group), typeof(GroupMember), typeof(PermissionTarget), typeof(PermissionGrant), typeof(ProjectMember),
        typeof(Project), typeof(Repository), typeof(Organization), typeof(OrganizationMember),
    };

    private readonly AccessModelVersion _version;
    private readonly ConditionalWeakTable<DbContext, object> _pending = new();

    public AccessModelSaveChangesInterceptor(AccessModelVersion version)
    {
        _version = version ?? throw new ArgumentNullException(nameof(version));
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Mark(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Mark(eventData.Context);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Commit(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Commit(eventData.Context);
        return new ValueTask<int>(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    private void Mark(DbContext? context)
    {
        if (context == null) return;

        var changed = context.ChangeTracker.Entries().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            && Relevant.Contains(e.Entity.GetType()));

        if (changed) _pending.AddOrUpdate(context, this);
    }

    private void Commit(DbContext? context)
    {
        if (context != null && _pending.Remove(context)) _version.Bump();
    }

    private void Forget(DbContext? context)
    {
        if (context != null) _pending.Remove(context);
    }
}
