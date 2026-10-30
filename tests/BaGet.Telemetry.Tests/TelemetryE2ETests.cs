using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace BaGet.Telemetry.Tests;

/// <summary>
/// Telemetry against real backends: Jaeger for traces, an OpenTelemetry Collector (debug exporter, read back from its
/// log) for metrics and logs. Each test runs a real host and turns telemetry on through the Settings service, with no
/// restart. They share one collection: the "BaGet" ActivitySource and the Entity Framework listener are process-wide, so
/// two hosts running in parallel would feed each other's providers.
/// </summary>
[Collection(Name)]
public class TelemetryE2ETests(ITestOutputHelper output)
{
    public const string Name = "Telemetry e2e";

    private static readonly TimeSpan Visible = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("Grpc")]
    [InlineData("HttpProtobuf")]
    public async Task EnablingAtRuntimeShowsAPushsTraceInJaegerWithinTenSecondsWithoutARestart(string protocol)
    {
        var jaeger = await Containers.JaegerAsync();
        await using var host = TelemetryHost.Start(output);

        // Off: nothing is exported.
        var (before, _) = await host.PushAsync();
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Null(await TelemetryHost.JaegerTracesAsync(jaeger, host.ServiceName));
        Assert.False(host.Status.Get(TelemetrySignal.Traces).Active);

        await host.EnableAsync(protocol == "Grpc" ? jaeger.GrpcUrl : jaeger.HttpUrl, protocol);
        Assert.True(host.Status.Get(TelemetrySignal.Traces).Active);

        var (id, _) = await host.PushAsync();
        var waited = await host.WaitForTraceAsync(jaeger, id, Visible);
        output.WriteLine($"{protocol}: trace visible after {waited.TotalSeconds:0.0} s");

        // The earlier push (made while off) was not exported.
        var json = (await TelemetryHost.JaegerTracesAsync(jaeger, host.ServiceName))!;
        Assert.DoesNotContain(before, json);

        await WaitUntilAsync(() => host.Status.Get(TelemetrySignal.Traces).LastSuccess != null);
        Assert.Null(host.Status.Get(TelemetrySignal.Traces).FailingSince);
    }

    [Fact]
    public async Task AnUnreachableEndpointLeavesRequestsAloneShowsFailingAndRestoringResumes()
    {
        var jaeger = await Containers.JaegerAsync();
        await using var host = TelemetryHost.Start(output);
        await host.EnableAsync(jaeger.GrpcUrl);

        var (first, _) = await host.PushAsync();
        await host.WaitForTraceAsync(jaeger, first, Visible);

        // Requests are not slowed by a dead endpoint: compare medians on the same host, telemetry on both times.
        var healthy = await MedianPushMillisecondsAsync(host, 25);
        await host.EnableAsync("http://127.0.0.1:1");
        var broken = await MedianPushMillisecondsAsync(host, 25);
        output.WriteLine($"median push: healthy {healthy:0.0} ms, unreachable {broken:0.0} ms");
        Assert.True(broken < healthy * 1.5 + 5, $"requests slowed by the dead endpoint: {healthy:0.0} -> {broken:0.0} ms");

        host.App.Services.GetRequiredService<TracerProvider>().ForceFlush(15_000);
        await WaitUntilAsync(() => host.Status.Get(TelemetrySignal.Traces).FailingSince != null);
        var traces = host.Status.Get(TelemetrySignal.Traces);
        Assert.True(traces.Active);
        Assert.False(string.IsNullOrEmpty(traces.LastError));
        Assert.DoesNotContain("/v1/", traces.LastError);

        // Restoring the endpoint resumes export, and the history starts fresh.
        await host.EnableAsync(jaeger.GrpcUrl);
        var (resumed, _) = await host.PushAsync();
        await host.WaitForTraceAsync(jaeger, resumed, Visible);
        await WaitUntilAsync(() => host.Status.Get(TelemetrySignal.Traces).LastSuccess != null);
        Assert.Null(host.Status.Get(TelemetrySignal.Traces).FailingSince);
    }

    [Fact]
    public async Task JaegerRejectsMetricsAndLogsSoTheirStatusFailsWhileTracesStayHealthy()
    {
        var jaeger = await Containers.JaegerAsync();
        await using var host = TelemetryHost.Start(output);

        // B5: pointing the metrics and logs at Jaeger (traces only) must not be silent.
        await host.EnableAsync(jaeger.GrpcUrl, metrics: jaeger.GrpcUrl, logs: jaeger.GrpcUrl);
        var (id, _) = await host.PushAsync();
        await host.WaitForTraceAsync(jaeger, id, Visible);

        await WaitUntilAsync(() =>
        {
            host.App.Services.GetRequiredService<MeterProvider>().ForceFlush(5_000);
            return host.Status.Get(TelemetrySignal.Metrics).FailingSince != null;
        });
        var metrics = host.Status.Get(TelemetrySignal.Metrics);
        output.WriteLine("metrics against Jaeger: " + metrics.LastError);

        await WaitUntilAsync(() => host.Status.Get(TelemetrySignal.Logs).FailingSince != null);
        output.WriteLine("logs against Jaeger: " + host.Status.Get(TelemetrySignal.Logs).LastError);
        Assert.Null(host.Status.Get(TelemetrySignal.Traces).FailingSince);
    }

    [Fact]
    public async Task MetricsAndLogsAreExportedOnlyWhileTheirOwnEndpointIsSet()
    {
        var jaeger = await Containers.JaegerAsync();
        var collector = await Containers.CollectorAsync();
        await using var host = TelemetryHost.Start(output);

        // Traces only: the collector must see neither metrics nor logs of this service.
        await host.EnableAsync(jaeger.GrpcUrl);
        var (silent, _) = await host.PushAsync();
        await host.WaitForTraceAsync(jaeger, silent, Visible);
        await Task.Delay(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);   // several 1 s metric intervals
        Assert.DoesNotContain(host.ServiceName, await collector.LogsAsync());
        Assert.False(host.Status.Get(TelemetrySignal.Metrics).Active);
        Assert.False(host.Status.Get(TelemetrySignal.Logs).Active);

        // Endpoints set: both arrive, still without a restart.
        await host.EnableAsync(jaeger.GrpcUrl, metrics: collector.GrpcUrl, logs: collector.GrpcUrl);
        var (loud, _) = await host.PushAsync();
        await host.WaitForTraceAsync(jaeger, loud, Visible);

        var received = await TelemetryHost.PollAsync(async () =>
        {
            var text = await collector.LogsAsync();
            return text.Contains(loud) && text.Contains("baget.packages.pushed") ? text : null;
        }, TimeSpan.FromSeconds(30), "metrics and logs in the collector");

        Assert.Contains($"service.name: Str({host.ServiceName})", received);
        await WaitUntilAsync(() => host.Status.Get(TelemetrySignal.Metrics).LastSuccess != null);
        await WaitUntilAsync(() => host.Status.Get(TelemetrySignal.Logs).LastSuccess != null);

        // Emptying them again stops the export.
        await host.EnableAsync(jaeger.GrpcUrl);
        Assert.False(host.Status.Get(TelemetrySignal.Metrics).Active);
        Assert.False(host.Status.Get(TelemetrySignal.Logs).Active);
    }

    [Fact]
    public async Task AStalledCollectorKeepsMemoryBoundedAndCostsRequestsAtMostFivePercent()
    {
        // A listener that accepts and never answers: exports hang until their timeout, so the span queue fills.
        using var stalled = new TcpListener(IPAddress.Loopback, 0);
        stalled.Start();
        var accepted = new List<TcpClient>();
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var client = await stalled.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                    lock (accepted) accepted.Add(client);
                }
            }
            catch (Exception)
            {
                // Listener stopped.
            }
        }, TestContext.Current.CancellationToken);
        var blackhole = "http://127.0.0.1:" + ((IPEndPoint)stalled.LocalEndpoint).Port;

        await using var host = TelemetryHost.Start(output);
        await host.EnableAsync(blackhole);

        // Warm up (JIT, connection pools, the SQLite file).
        await MedianPushMillisecondsAsync(host, 30);

        // Off/on blocks alternate (and swap order every round) so drift and neighbours' load hit both equally; the verdict is
        // the median of the per-round on/off ratios. A real regression shows in every attempt, a noisy machine in one.
        var memory = new List<long>();
        var ratio = double.MaxValue;
        for (var attempt = 1; attempt <= 3 && ratio > 1.05; attempt++)
        {
            var ratios = new List<double>();
            var off = new List<double>();
            var on = new List<double>();
            for (var round = 0; round < 8; round++)
            {
                double offBlock = 0, onBlock = 0;
                foreach (var enabled in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
                {
                    await host.SaveAsync(("Telemetry:Enabled", enabled));
                    var block = await PushMillisecondsAsync(host, 25);
                    (enabled ? on : off).AddRange(block);
                    if (enabled) onBlock = Median(block); else offBlock = Median(block);
                }

                ratios.Add(onBlock / offBlock);
                memory.Add(GC.GetTotalMemory(forceFullCollection: true));
            }

            ratio = Median(ratios);
            output.WriteLine($"attempt {attempt}: median push over {off.Count} requests: off {Median(off):0.00} ms, on with a stalled collector {Median(on):0.00} ms; median per-round ratio {ratio - 1:P1}");
        }

        output.WriteLine("managed memory per round (MB): " + string.Join(", ", memory.Select(m => (m / 1048576.0).ToString("0.0"))));

        // Flood the stalled exporter: far more spans than the 2048-slot queue hold, so spans are dropped, not kept.
        await host.SaveAsync(("Telemetry:Enabled", true));
        for (var i = 0; i < 400; i++) await host.PushAsync();
        memory.Add(GC.GetTotalMemory(forceFullCollection: true));
        var traces = host.Status.Get(TelemetrySignal.Traces);
        output.WriteLine($"dropped spans: {traces.Dropped}");
        Assert.True(traces.Dropped > 0, "the bounded queue never overflowed; the load did not exercise it");

        Assert.True(ratio <= 1.05, $"latency regression above 5 % in 3 attempts: {ratio - 1:P1}");

        // Bounded: once the queue has filled (first rounds) memory stops growing with the number of pushes.
        var settled = memory.Skip(2).ToArray();   // the queue fills in the first rounds
        Assert.True(settled.Max() - settled.Min() < 40L * 1024 * 1024, "managed memory kept growing under load: " + string.Join(", ", memory));
    }

    private static async Task<double> MedianPushMillisecondsAsync(TelemetryHost host, int count)
        => Median(await PushMillisecondsAsync(host, count));

    private static async Task<List<double>> PushMillisecondsAsync(TelemetryHost host, int count)
    {
        var times = new List<double>(count);
        for (var i = 0; i < count; i++) times.Add((await host.PushAsync()).Elapsed.TotalMilliseconds);
        return times;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition was not met within " + seconds + " s.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}
