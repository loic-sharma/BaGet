using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Telemetry;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The live exporter swap (B3): a rebuilt exporter loses no batch, is disposed exactly once and never while exporting,
/// and flush/shutdown reach the current one; nothing is built or exported while the signal is off.
/// </summary>
public class ReloadableExporterTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables) disposable.Dispose();
    }

    private static TelemetryOptions On(string endpoint = "http://collector:4317", string? headers = null) => new()
    {
        Enabled = true,
        Otlp = new OtlpOptions { Endpoint = endpoint, Headers = headers },
        Metrics = new MetricsOptions { Endpoint = endpoint },
        Logs = new LogsOptions { Endpoint = endpoint },
    };

    private static IEnumerable<Activity> MakeActivities(int count)
    {
        using var source = new ActivitySource("ReloadableExporterTests");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "ReloadableExporterTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var result = new List<Activity>();
        for (var i = 0; i < count; i++)
        {
            var activity = source.StartActivity("span " + i)!;
            activity.Stop();
            result.Add(activity);
        }

        return result;
    }

    [Fact]
    public void WhileOffNothingIsBuiltAndExportSucceedsWithoutAnInnerExporter()
    {
        var factory = new RecordingFactory();
        var monitor = new TestOptionsMonitor(new TelemetryOptions());
        var status = new TelemetryStatus();

        using var exporter = new ReloadableTraceExporter(monitor, status, factory);

        Assert.Empty(factory.Created);
        Assert.Null(exporter.Inner);
        Assert.False(status.Get(TelemetrySignal.Traces).Active);
    }

    [Fact]
    public void AnEnabledTelemetryWithoutAnEndpointIsStillOff()
    {
        var factory = new RecordingFactory();
        var options = On();
        options.Otlp.Endpoint = "";
        using var exporter = new ReloadableTraceExporter(new TestOptionsMonitor(options), new TelemetryStatus(), factory);

        Assert.Empty(factory.Created);
    }

    [Fact]
    public async Task ASwapWhileABatchIsExportingKeepsTheOldExporterAliveUntilItReturnsThenDisposesItOnce()
    {
        var factory = new RecordingFactory { BlockFirst = true };
        var monitor = new TestOptionsMonitor(On("http://one:4317"));
        using var exporter = new ReloadableTraceExporter(monitor, new TelemetryStatus(), factory);
        var first = factory.Created.Single();

        var activities = MakeActivities(3).ToArray();
        var export = Task.Run(() => exporter.Export(new Batch<Activity>(activities, activities.Length)), TestContext.Current.CancellationToken);
        Assert.True(first.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "the export never started");

        // The endpoint changes while the export is still running in the old exporter.
        monitor.Set(On("http://two:4317"));

        Assert.Equal(2, factory.Created.Count);
        Assert.Equal(0, first.DisposeCount);
        Assert.NotSame(first, exporter.Inner);

        first.Release.Set();
        Assert.Equal(ExportResult.Success, await export);

        Assert.Equal(3, first.Exported.Count);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(0, factory.Created[1].DisposeCount);
    }

    [Fact]
    public async Task NoBatchIsLostOrDoubleDisposedWhenExportersAreSwappedUnderABatchProcessor()
    {
        var factory = new RecordingFactory();
        var monitor = new TestOptionsMonitor(On("http://0:4317"));
        var exporter = new ReloadableTraceExporter(monitor, new TelemetryStatus(), factory);
        var processor = new BatchActivityExportProcessor(exporter, maxQueueSize: 100_000, scheduledDelayMilliseconds: 20, exporterTimeoutMilliseconds: 10_000, maxExportBatchSize: 16);

        const int total = 2000;
        var activities = MakeActivities(total).ToArray();
        using var stop = new CancellationTokenSource();
        var swapper = Task.Run(() =>
        {
            for (var i = 1; !stop.IsCancellationRequested; i++)
            {
                monitor.Set(On("http://" + i + ":4317"));
                Thread.Sleep(1);
            }
        }, TestContext.Current.CancellationToken);

        for (var i = 0; i < activities.Length; i++)
        {
            processor.OnEnd(activities[i]);

            // Swaps interleaved with the batch thread exporting, whatever the scheduler does to the swapper task.
            if (i % 25 == 0) monitor.Set(On("http://producer" + i + ":4317"));
        }

        Assert.True(processor.ForceFlush(30_000));
        stop.Cancel();
        await swapper;
        Assert.True(processor.Shutdown(30_000));
        processor.Dispose();

        var exported = factory.Created.SelectMany(e => e.Exported).ToArray();
        Assert.Equal(total, exported.Length);
        Assert.Equal(total, exported.Select(a => a.SpanId).Distinct().Count());
        Assert.True(factory.Created.Count > 2, "the exporter was never swapped");
        Assert.All(factory.Created, e => Assert.Equal(1, e.DisposeCount));
        Assert.All(factory.Created, e => Assert.False(e.ExportedAfterDispose, "an export ran on a disposed exporter"));
    }

    [Fact]
    public void ForceFlushAndShutdownReachTheCurrentInnerExporterAndItIsDisposedOnce()
    {
        var factory = new RecordingFactory();
        var monitor = new TestOptionsMonitor(On("http://one:4317"));
        var exporter = new ReloadableTraceExporter(monitor, new TelemetryStatus(), factory);
        monitor.Set(On("http://two:4317"));
        var (old, current) = (factory.Created[0], factory.Created[1]);

        Assert.True(exporter.ForceFlush(1000));
        Assert.Equal(0, old.ForceFlushes);
        Assert.Equal(1, current.ForceFlushes);

        Assert.True(exporter.Shutdown(1000));
        Assert.Equal(1, current.Shutdowns);

        exporter.Dispose();
        exporter.Dispose();
        Assert.Equal(1, current.DisposeCount);
        Assert.Equal(1, old.DisposeCount);

        // Nothing is built after shutdown, whatever the configuration does.
        monitor.Set(On("http://three:4317"));
        Assert.Equal(2, factory.Created.Count);
    }

    [Fact]
    public void TheChangeSubscriptionIsDisposedWithTheExporterAndTheSink()
    {
        var monitor = new TestOptionsMonitor(On());
        var exporter = new ReloadableTraceExporter(monitor, new TelemetryStatus(), new RecordingFactory());
        var metrics = new ReloadableMetricExporter(monitor, new TelemetryStatus(), new RecordingFactory());
        var sink = new ReloadableOpenTelemetrySink(monitor, new TelemetryStatus(), new RecordingFactory());
        Assert.Equal(3, monitor.Subscriptions);

        exporter.Dispose();
        metrics.Dispose();
        sink.Dispose();

        Assert.Equal(0, monitor.Subscriptions);
    }

    [Fact]
    public void AnUnrelatedConfigurationReloadDoesNotRebuildTheExporter()
    {
        var factory = new RecordingFactory();
        var monitor = new TestOptionsMonitor(On());
        using var exporter = new ReloadableTraceExporter(monitor, new TelemetryStatus(), factory);

        monitor.Set(On());
        monitor.Set(On());

        Assert.Single(factory.Created);
    }

    [Fact]
    public void ChangingTheHeadersTimeoutOrProtocolRebuildsAndSwitchingOffDisposesWithoutNetwork()
    {
        var factory = new RecordingFactory();
        var monitor = new TestOptionsMonitor(On());
        using var exporter = new ReloadableTraceExporter(monitor, new TelemetryStatus(), factory);

        monitor.Set(On(headers: "k=v"));
        var timeout = On(headers: "k=v");
        timeout.Otlp.Timeout = TimeSpan.FromSeconds(3);
        monitor.Set(timeout);
        var protocol = On(headers: "k=v");
        protocol.Otlp.Protocol = "HttpProtobuf";
        protocol.Otlp.Timeout = TimeSpan.FromSeconds(3);
        monitor.Set(protocol);
        Assert.Equal(4, factory.Created.Count);

        monitor.Set(new TelemetryOptions());

        Assert.Null(exporter.Inner);
        Assert.Equal(4, factory.Created.Count);
        Assert.All(factory.Created, e => Assert.Equal(1, e.DisposeCount));

        var activities = MakeActivities(2).ToArray();
        Assert.Equal(ExportResult.Success, exporter.Export(new Batch<Activity>(activities, activities.Length)));
        Assert.All(factory.Created, e => Assert.Empty(e.Exported));
    }

    [Fact]
    public void AFailingExportIsRecordedAndAScrubbedErrorNeverShowsTheHeaders()
    {
        var factory = new RecordingFactory { Throw = new InvalidOperationException("bad key TOP-SECRET-1234 at http://user:pw@collector:4317/v1/traces?token=abc") };
        var status = new TelemetryStatus();
        using var exporter = new ReloadableTraceExporter(new TestOptionsMonitor(On(headers: "api-key=TOP-SECRET-1234")), status, factory);
        var activities = MakeActivities(1).ToArray();

        Assert.Equal(ExportResult.Failure, exporter.Export(new Batch<Activity>(activities, activities.Length)));

        var traces = status.Get(TelemetrySignal.Traces);
        Assert.NotNull(traces.FailingSince);
        Assert.NotNull(traces.LastError);
        Assert.DoesNotContain("TOP-SECRET-1234", traces.LastError);
        Assert.DoesNotContain("user", traces.LastError!.Replace("InvalidOperationException", ""));
        Assert.DoesNotContain("token=abc", traces.LastError);
        Assert.DoesNotContain("/v1/traces", traces.LastError);
        Assert.Contains("http://collector:4317", traces.LastError);
    }

    [Fact]
    public void ASuccessfulExportClearsTheFailure()
    {
        var factory = new RecordingFactory { Throw = new InvalidOperationException("down") };
        var status = new TelemetryStatus();
        using var exporter = new ReloadableTraceExporter(new TestOptionsMonitor(On()), status, factory);
        var activities = MakeActivities(1).ToArray();
        exporter.Export(new Batch<Activity>(activities, activities.Length));
        Assert.NotNull(status.Get(TelemetrySignal.Traces).FailingSince);

        factory.Created.Single().Throw = null;
        exporter.Export(new Batch<Activity>(activities, activities.Length));

        var traces = status.Get(TelemetrySignal.Traces);
        Assert.Null(traces.FailingSince);
        Assert.Null(traces.LastError);
        Assert.NotNull(traces.LastSuccess);
    }

    [Fact]
    public void ALogEventBelowTheConfiguredLevelOrFromTheExportersOwnHttpClientIsNotSent()
    {
        var factory = new RecordingFactory();
        var options = On();
        options.Logs.MinimumLevel = "Warning";
        using var sink = new ReloadableOpenTelemetrySink(new TestOptionsMonitor(options), new TelemetryStatus(), factory);

        sink.Emit(Event(LogEventLevel.Information, null));
        sink.Emit(Event(LogEventLevel.Warning, "System.Net.Http.HttpClient.x"));
        sink.Emit(Event(LogEventLevel.Warning, "Grpc.Net.Client.Internal"));
        sink.Emit(Event(LogEventLevel.Warning, "BaGet.Something"));
        sink.Emit(Event(LogEventLevel.Error, null));

        Assert.Equal(2, factory.Sinks.Single().Events.Count);
    }

    [Fact]
    public void ALogSinkIsRebuiltOnEndpointChangeAndFlushedOnce()
    {
        var factory = new RecordingFactory();
        var monitor = new TestOptionsMonitor(On("http://one:4317"));
        var sink = new ReloadableOpenTelemetrySink(monitor, new TelemetryStatus(), factory);
        monitor.Set(On("http://two:4317"));
        sink.Dispose();
        sink.Dispose();

        Assert.Equal(2, factory.Sinks.Count);
        Assert.All(factory.Sinks, s => Assert.Equal(1, s.DisposeCount));
    }

    [Fact]
    public async Task ReplacingALogSinkNeverWaitsForTheRetiredSinkToDrain()
    {
        var factory = new RecordingFactory { BlockSinkDispose = true };
        var monitor = new TestOptionsMonitor(On("http://one:4317"));
        var sink = new ReloadableOpenTelemetrySink(monitor, new TelemetryStatus(), factory);

        // The old sink's disposal (a drain against a stalled collector) is blocked: the swap, which runs inside the
        // settings save, must still return at once.
        var swap = Task.Run(() => monitor.Set(On("http://two:4317")), TestContext.Current.CancellationToken);
        var finished = await Task.WhenAny(swap, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)) == swap;
        foreach (var created in factory.Sinks) created.ReleaseDispose.Set();

        Assert.True(finished, "the swap waited for the retired sink to be disposed");
        sink.Dispose();
        Assert.All(factory.Sinks, s => Assert.Equal(1, s.DisposeCount));
    }

    [Fact]
    public async Task DisposeWaitsForASinkWhoseLeaseAnEmitHoldsAcrossTheClose()
    {
        var factory = new RecordingFactory { BlockSinkEmit = true };
        var sink = new ReloadableOpenTelemetrySink(new TestOptionsMonitor(On()), new TelemetryStatus(), factory);
        var recording = factory.Sinks.Single();

        var emit = Task.Run(() => sink.Emit(Event(LogEventLevel.Error, null)), TestContext.Current.CancellationToken);
        Assert.True(recording.EmitEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        // The close happens while the emit holds the lease: the sink is retired only when the emit returns.
        var dispose = Task.Run(sink.Dispose, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.False(dispose.IsCompleted, "Dispose returned before the sink in use was drained");

        recording.ReleaseEmit.Set();
        await emit;
        Assert.True(await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)) == dispose);
        Assert.Equal(1, recording.DisposeCount);
    }

    [Fact]
    public void ChangingTheServiceNameNeitherRebuildsTheLogSinkNorChangesItsResource()
    {
        var factory = new RecordingFactory();
        var first = On("http://one:4317");
        first.ServiceName = "startup-name";
        var monitor = new TestOptionsMonitor(first);
        using var sink = new ReloadableOpenTelemetrySink(monitor, new TelemetryStatus(), factory);

        var renamed = On("http://one:4317");
        renamed.ServiceName = "renamed";
        monitor.Set(renamed);
        var moved = On("http://two:4317");
        moved.ServiceName = "renamed-again";
        monitor.Set(moved);

        Assert.Equal(2, factory.Sinks.Count);
        Assert.All(factory.ServiceNames, name => Assert.Equal("startup-name", name));
    }

    private static LogEvent Event(LogEventLevel level, string? sourceContext)
    {
        var properties = new List<LogEventProperty>();
        if (sourceContext != null) properties.Add(new LogEventProperty("SourceContext", new ScalarValue(sourceContext)));
        return new LogEvent(DateTimeOffset.UtcNow, level, null, new MessageTemplate("m", Array.Empty<MessageTemplateToken>()), properties);
    }

    /// <summary>An <see cref="IOptionsMonitor{TOptions}"/> whose value the test sets, notifying subscribers.</summary>
    private sealed class TestOptionsMonitor : IOptionsMonitor<TelemetryOptions>
    {
        private readonly List<Action<TelemetryOptions, string?>> _listeners = new();
        private TelemetryOptions _current;

        public TestOptionsMonitor(TelemetryOptions current) => _current = current;

        public TelemetryOptions CurrentValue => _current;

        public int Subscriptions
        {
            get
            {
                lock (_listeners) return _listeners.Count;
            }
        }

        public TelemetryOptions Get(string? name) => _current;

        public IDisposable OnChange(Action<TelemetryOptions, string?> listener)
        {
            lock (_listeners) _listeners.Add(listener);
            return new Subscription(this, listener);
        }

        public void Set(TelemetryOptions value)
        {
            _current = value;
            Action<TelemetryOptions, string?>[] snapshot;
            lock (_listeners) snapshot = _listeners.ToArray();
            foreach (var listener in snapshot) listener(value, null);
        }

        private sealed class Subscription : IDisposable
        {
            private readonly TestOptionsMonitor _monitor;
            private readonly Action<TelemetryOptions, string?> _listener;

            public Subscription(TestOptionsMonitor monitor, Action<TelemetryOptions, string?> listener)
            {
                _monitor = monitor;
                _listener = listener;
            }

            public void Dispose()
            {
                lock (_monitor._listeners) _monitor._listeners.Remove(_listener);
            }
        }
    }

    private sealed class RecordingFactory : ITelemetryExporterFactory
    {
        public List<RecordingExporter> Created { get; } = new();

        public List<RecordingSink> Sinks { get; } = new();

        public List<string> ServiceNames { get; } = new();

        /// <summary>The first exporter built blocks its first export until released.</summary>
        public bool BlockFirst { get; set; }

        public Exception? Throw { get; set; }

        /// <summary>Sinks block in Dispose until their <c>ReleaseDispose</c> is set.</summary>
        public bool BlockSinkDispose { get; set; }

        /// <summary>Sinks block in Emit until their <c>ReleaseEmit</c> is set.</summary>
        public bool BlockSinkEmit { get; set; }

        public BaseExporter<Activity> CreateTraceExporter(TelemetryOptions options, Uri endpoint)
        {
            var exporter = new RecordingExporter { Block = BlockFirst && Created.Count == 0, Throw = Throw };
            lock (Created) Created.Add(exporter);
            return exporter;
        }

        public BaseExporter<Metric> CreateMetricExporter(TelemetryOptions options, Uri endpoint) => new NoMetrics();

        public ILogEventSink CreateLogSink(TelemetryOptions options, Uri endpoint, string serviceName, TelemetryStatus.Signal status)
        {
            ServiceNames.Add(serviceName);
            var sink = new RecordingSink { BlockDispose = BlockSinkDispose, BlockEmit = BlockSinkEmit };
            lock (Sinks) Sinks.Add(sink);
            return sink;
        }
    }

    private sealed class NoMetrics : BaseExporter<Metric>
    {
        public override ExportResult Export(in Batch<Metric> batch) => ExportResult.Success;
    }

    private sealed class RecordingSink : ILogEventSink, IDisposable
    {
        private int _dispose;
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public int DisposeCount => Volatile.Read(ref _dispose);
        public bool BlockDispose { get; set; }
        public ManualResetEventSlim ReleaseDispose { get; } = new();
        public bool BlockEmit { get; set; }
        public ManualResetEventSlim EmitEntered { get; } = new();
        public ManualResetEventSlim ReleaseEmit { get; } = new();

        public void Emit(LogEvent logEvent)
        {
            EmitEntered.Set();
            if (BlockEmit) ReleaseEmit.Wait(TimeSpan.FromSeconds(30));
            Events.Enqueue(logEvent);
        }

        public void Dispose()
        {
            if (BlockDispose) ReleaseDispose.Wait(TimeSpan.FromSeconds(30));
            Interlocked.Increment(ref _dispose);
        }
    }

    private sealed class RecordingExporter : BaseExporter<Activity>
    {
        private int _dispose;
        private int _flushes;
        private int _shutdowns;

        public ConcurrentQueue<Activity> ExportedQueue { get; } = new();
        public IReadOnlyCollection<Activity> Exported => ExportedQueue.ToArray();
        public bool Block { get; set; }
        public Exception? Throw { get; set; }
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public bool ExportedAfterDispose { get; private set; }
        public int DisposeCount => Volatile.Read(ref _dispose);
        public int ForceFlushes => Volatile.Read(ref _flushes);
        public int Shutdowns => Volatile.Read(ref _shutdowns);

        public override ExportResult Export(in Batch<Activity> batch)
        {
            if (DisposeCount > 0) ExportedAfterDispose = true;
            if (Block)
            {
                Entered.Set();
                Release.Wait(TimeSpan.FromSeconds(30));
            }

            if (Throw != null) throw Throw;

            foreach (var activity in batch) ExportedQueue.Enqueue(activity);
            if (DisposeCount > 0) ExportedAfterDispose = true;
            return ExportResult.Success;
        }

        protected override bool OnForceFlush(int timeoutMilliseconds)
        {
            Interlocked.Increment(ref _flushes);
            return true;
        }

        protected override bool OnShutdown(int timeoutMilliseconds)
        {
            Interlocked.Increment(ref _shutdowns);
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Interlocked.Increment(ref _dispose);
            base.Dispose(disposing);
        }
    }
}
