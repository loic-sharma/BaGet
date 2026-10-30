using System;
using System.Diagnostics;
using BaGet.Core;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace BaGet.Telemetry;

/// <summary>
/// An exporter whose real (OTLP) exporter is rebuilt when <see cref="TelemetryOptions"/> change, because the OpenTelemetry
/// SDK builds its exporters once. The processor or reader in front of it (batch queue, metric reader) never changes.
/// </summary>
/// <remarks>
/// The inner exporter lives in a <see cref="SwappableHolder{T}"/>: a swap never waits for an export in flight, the old
/// exporter is disposed exactly once after its last export returns, and <see cref="OnForceFlush"/> /
/// <see cref="OnShutdown"/> reach whichever exporter is current. While the signal is off (telemetry disabled, no
/// endpoint) there is no inner exporter and <see cref="Export"/> returns success without touching the network.
/// The change subscription is disposed with the exporter.
/// </remarks>
internal abstract class ReloadableExporter<T> : BaseExporter<T> where T : class
{
    private readonly IOptionsMonitor<TelemetryOptions> _options;
    private readonly TelemetryStatus.Signal _status;
    private readonly TelemetrySignal _signal;
    private readonly Func<TelemetryOptions, Uri, BaseExporter<T>> _create;
    private readonly SwappableHolder<BaseExporter<T>> _holder = new();
    private readonly object _reloadLock = new();
    private readonly IDisposable? _subscription;
    private string? _key;
    private bool _disposed;

    protected ReloadableExporter(
        IOptionsMonitor<TelemetryOptions> options,
        TelemetryStatus.Signal status,
        TelemetrySignal signal,
        Func<TelemetryOptions, Uri, BaseExporter<T>> create)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _signal = signal;
        _create = create ?? throw new ArgumentNullException(nameof(create));

        Reload();
        _subscription = options.OnChange(_ => Reload());
    }

    /// <summary>The current inner exporter (null while off). For tests.</summary>
    internal BaseExporter<T>? Inner => _holder.Current;

    /// <summary>
    /// Rebuild the inner exporter if what it depends on changed (the monitor also fires for unrelated configuration
    /// reloads, which must not drop the connection). A failure to build leaves the signal off and is shown in the status.
    /// </summary>
    internal void Reload()
    {
        lock (_reloadLock)
        {
            if (_disposed) return;

            var options = TelemetryEndpoints.Read(_options);
            var key = options == null ? null : TelemetryEndpoints.Key(options, _signal);
            if (_initialized && key == _key) return;

            _initialized = true;
            BaseExporter<T>? inner = null;
            if (key != null)
            {
                try
                {
                    inner = _create(options!, TelemetryEndpoints.Resolve(options!, _signal)!);
                    Adopt(inner);
                }
                catch (Exception e)
                {
                    _status.Failure(TelemetryStatus.Describe(e, options!.Otlp?.Headers));
                    _holder.Swap(null);
                    _key = key;
                    return;
                }
            }

            _key = key;
            _holder.Swap(inner);
            _status.Configured(inner != null);
        }
    }

    private bool _initialized;

    private static readonly System.Reflection.MethodInfo? SetParentProvider
        = typeof(BaseExporter<T>).GetProperty(nameof(ParentProvider))?.GetSetMethod(nonPublic: true);

    /// <summary>
    /// Hands the inner exporter the provider the SDK gave this one: it carries the resource (service.name...). The setter is
    /// internal to the SDK, which sets it on the exporter it was given, i.e. this wrapper, not on what it wraps. If a future
    /// SDK removes it, exports still work but carry an empty resource.
    /// </summary>
    private void Adopt(BaseExporter<T> inner)
    {
        if (ParentProvider != null && !ReferenceEquals(inner.ParentProvider, ParentProvider))
        {
            SetParentProvider?.Invoke(inner, new object[] { ParentProvider });
        }
    }

    public override ExportResult Export(in Batch<T> batch)
    {
        using var lease = _holder.Acquire();
        if (lease == null) return ExportResult.Success;

        // The SDK sets ParentProvider (it carries the resource) after construction.
        Adopt(lease.Value);

        try
        {
            var result = lease.Value.Export(batch);
            if (result == ExportResult.Success)
            {
                _status.Success();
            }
            else
            {
                _status.Failure("The collector could not be reached or rejected the export.");
            }

            return result;
        }
        catch (Exception e)
        {
            _status.Failure(TelemetryStatus.Describe(e, TelemetryEndpoints.Read(_options)?.Otlp?.Headers));
            return ExportResult.Failure;
        }
    }

    protected override bool OnForceFlush(int timeoutMilliseconds)
    {
        using var lease = _holder.Acquire();
        return lease == null || lease.Value.ForceFlush(timeoutMilliseconds);
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        bool result;
        using (var lease = _holder.Acquire())
        {
            result = lease == null || lease.Value.Shutdown(timeoutMilliseconds);
        }

        // Nothing may be started again after a shutdown.
        _holder.Close();
        return result;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_reloadLock) _disposed = true;
            _subscription?.Dispose();
            _holder.Close();
        }

        base.Dispose(disposing);
    }
}

/// <summary>The reloadable exporter of spans.</summary>
internal sealed class ReloadableTraceExporter : ReloadableExporter<Activity>
{
    public ReloadableTraceExporter(IOptionsMonitor<TelemetryOptions> options, TelemetryStatus status, ITelemetryExporterFactory factory)
        : base(options, status.Traces, TelemetrySignal.Traces, factory.CreateTraceExporter)
    {
    }
}

/// <summary>The reloadable exporter of metrics.</summary>
internal sealed class ReloadableMetricExporter : ReloadableExporter<Metric>
{
    public ReloadableMetricExporter(IOptionsMonitor<TelemetryOptions> options, TelemetryStatus status, ITelemetryExporterFactory factory)
        : base(options, status.Metrics, TelemetrySignal.Metrics, factory.CreateMetricExporter)
    {
    }
}
