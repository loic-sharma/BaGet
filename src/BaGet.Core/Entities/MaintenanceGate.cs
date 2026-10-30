using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Core;

/// <summary>
/// An in-memory, process-wide read-only switch for the database. While it is closed,
/// <see cref="MaintenanceSaveChangesInterceptor"/> refuses every <c>SaveChanges</c> with a
/// <see cref="MaintenanceException"/>; reads are unaffected.
/// <para>
/// In a cluster (<c>Ha:Enabled</c>) a closure made by <see cref="Close"/> is published to the other nodes and theirs are applied here
/// (<see cref="ClusterSyncService"/>), so the whole cluster is read-only together; the closing node keeps it alive and a node that
/// dies stops holding it. <see cref="CloseUntilRestart"/> stays on its own node.
/// </para>
/// <para>
/// A context built with <see cref="AllowWrites(DbContextOptionsBuilder)"/> is exempt, so the job that
/// holds the gate closed (for example a database copy) can still write to its own target.
/// </para>
/// <para>
/// <see cref="CloseUntilRestart"/> closes the gate for good: <see cref="Open"/> then does nothing,
/// because the state is not persisted and a restart is the only thing that resets it.
/// </para>
/// </summary>
public sealed class MaintenanceGate
{
    /// <summary>Seconds advertised in the <c>Retry-After</c> header of a 503 answered while the gate is closed.</summary>
    public const int RetryAfterSeconds = 60;

    private readonly object _lock = new();
    private string? _reason;
    private bool _untilRestart;
    private bool _remote;

    /// <summary>Whether writes are currently refused.</summary>
    public bool IsClosed { get { lock (_lock) return _reason != null; } }

    /// <summary>True when the gate was closed with <see cref="CloseUntilRestart"/> and cannot be reopened.</summary>
    public bool IsClosedUntilRestart { get { lock (_lock) return _untilRestart; } }

    /// <summary>Why the gate is closed, or null while it is open.</summary>
    public string? Reason { get { lock (_lock) return _reason; } }

    /// <summary>
    /// Called with the reason (or null when reopened) after this node closes or reopens the gate itself, to publish it to the cluster.
    /// </summary>
    internal Action<string?>? Publisher { get; set; }

    /// <summary>The reason of a closure this node made itself (to keep it published), or null.</summary>
    internal string? LocalReason { get { lock (_lock) return _remote || _untilRestart ? null : _reason; } }

    /// <summary>Refuse writes until <see cref="Open"/> is called.</summary>
    public void Close(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required.", nameof(reason));

        var publish = false;
        lock (_lock)
        {
            // A permanent closure keeps its own reason.
            if (!_untilRestart)
            {
                _reason = reason;
                _remote = false;
                publish = true;
            }
        }

        if (publish) Publisher?.Invoke(reason);
    }

    /// <summary>Applies the cluster's closure (null = no node holds the gate closed); never touches a closure this node made.</summary>
    internal void ApplyRemote(string? reason)
    {
        lock (_lock)
        {
            if (_untilRestart) return;
            if (reason != null)
            {
                if (_reason == null || _remote)
                {
                    _reason = reason;
                    _remote = true;
                }
            }
            else if (_remote)
            {
                _reason = null;
                _remote = false;
            }
        }
    }

    /// <summary>Refuse writes until the process restarts; <see cref="Open"/> no longer reopens.</summary>
    public void CloseUntilRestart(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required.", nameof(reason));

        lock (_lock)
        {
            _reason = reason;
            _untilRestart = true;
        }
    }

    /// <summary>
    /// Allow writes again. Returns false, leaving the gate closed, after <see cref="CloseUntilRestart"/> or when another node of the
    /// cluster holds it closed (only that node reopens it).
    /// </summary>
    public bool Open()
    {
        lock (_lock)
        {
            if (_untilRestart || _remote) return false;
            _reason = null;
        }

        Publisher?.Invoke(null);
        return true;
    }

    /// <summary>Throw <see cref="MaintenanceException"/> while the gate is closed.</summary>
    public void ThrowIfClosed()
    {
        var reason = Reason;
        if (reason != null) throw new MaintenanceException(reason);
    }

    /// <summary>
    /// Mark a context's options as exempt from the gate. Use it only for a context that the job holding
    /// the gate closed writes to itself, such as the target of a database copy.
    /// </summary>
    public static DbContextOptionsBuilder AllowWrites(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder == null) throw new ArgumentNullException(nameof(optionsBuilder));

        var extension = optionsBuilder.Options.FindExtension<AllowWritesOptionsExtension>()
            ?? new AllowWritesOptionsExtension();
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return optionsBuilder;
    }

    /// <summary>Typed form of <see cref="AllowWrites(DbContextOptionsBuilder)"/>.</summary>
    public static DbContextOptionsBuilder<TContext> AllowWrites<TContext>(DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        AllowWrites((DbContextOptionsBuilder)optionsBuilder);
        return optionsBuilder;
    }

    /// <summary>Whether a context was built with <see cref="AllowWrites(DbContextOptionsBuilder)"/>.</summary>
    internal static bool AreWritesAllowed(DbContext? context)
    {
        return context?.GetService<IDbContextOptions>().FindExtension<AllowWritesOptionsExtension>() != null;
    }

    private sealed class AllowWritesOptionsExtension : IDbContextOptionsExtension
    {
        private DbContextOptionsExtensionInfo? _info;

        public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

        public void ApplyServices(IServiceCollection services)
        {
        }

        public void Validate(IDbContextOptions options)
        {
        }

        private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            public ExtensionInfo(IDbContextOptionsExtension extension) : base(extension) { }

            public override bool IsDatabaseProvider => false;

            public override string LogFragment => "AllowWrites ";

            public override int GetServiceProviderHashCode() => 0;

            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
                debugInfo["BaGet:AllowWrites"] = "true";
            }
        }
    }
}
