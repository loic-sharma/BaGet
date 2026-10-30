using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using OpenTelemetry;

namespace BaGet.Telemetry;

/// <summary>The outcome of a connection test. <see cref="Message"/> is scrubbed: no headers, credentials or URL paths.</summary>
public sealed record TelemetryTestResult(bool Ok, string Message);

/// <summary>
/// Sends one synthetic span to a collector with explicit settings (the Settings page's unsaved form values), so an
/// operator can check an endpoint, protocol and headers before saving. It reaches the collector with a TCP connect first
/// (the only way to tell "nothing listens there" from "the protocol is wrong") and then exports the span through the
/// same exporter the host would use. Nothing is saved or changed, and the test span is not traced by the host itself.
/// </summary>
public sealed class TelemetryConnectionTester
{
    private const string TestSourceName = "BaGet.Telemetry.Test";
    private static readonly ActivitySource TestSource = new(TestSourceName);

    private readonly ITelemetryExporterFactory _factory;

    internal TelemetryConnectionTester(ITelemetryExporterFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <param name="endpoint">The traces endpoint, as typed.</param>
    /// <param name="protocol"><c>Grpc</c> or <c>HttpProtobuf</c>.</param>
    /// <param name="headers">Extra request headers, <c>key=value,key2=value2</c>.</param>
    /// <param name="timeout">How long to wait; defaults to 10 seconds.</param>
    /// <param name="cancellationToken">Cancels the test.</param>
    public async Task<TelemetryTestResult> TestAsync(
        string? endpoint,
        string? protocol,
        string? headers,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        var options = new TelemetryOptions
        {
            Enabled = true,
            Otlp = new OtlpOptions { Endpoint = endpoint, Protocol = string.IsNullOrWhiteSpace(protocol) ? "Grpc" : protocol!, Headers = headers, Timeout = limit },
        };

        var uri = TelemetryEndpoints.Resolve(options, TelemetrySignal.Traces);
        if (uri == null)
        {
            return new TelemetryTestResult(false, "Enter an absolute http or https endpoint.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(limit);

        try
        {
            var reachable = await ProbeAsync(uri, timeoutSource.Token).ConfigureAwait(false);
            if (reachable != null) return new TelemetryTestResult(false, reachable);

            // The exporter blocks until the collector answers or the timeout passes.
            var exported = await Task.Run(() => ExportTestSpan(options, uri), timeoutSource.Token).WaitAsync(timeoutSource.Token).ConfigureAwait(false);
            return exported
                ? new TelemetryTestResult(true, "The collector accepted a test span.")
                : new TelemetryTestResult(false, "The collector refused the test span. Check the protocol and headers.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TelemetryTestResult(false, "The collector did not answer within " + limit.TotalSeconds.ToString("0.#") + " s.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new TelemetryTestResult(false, TelemetryStatus.Describe(e, headers));
        }
    }

    /// <summary>Null when something listens at the endpoint, otherwise why not.</summary>
    private static async Task<string?> ProbeAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(uri.Host, uri.Port, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (SocketException e)
        {
            return e.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData => "The host " + uri.Host + " was not found.",
                SocketError.ConnectionRefused => "Nothing listens at " + uri.Authority + " (connection refused).",
                SocketError.TimedOut => "The connection to " + uri.Authority + " timed out.",
                _ => "Cannot connect to " + uri.Authority + " (" + e.SocketErrorCode + ").",
            };
        }
    }

    private bool ExportTestSpan(TelemetryOptions options, Uri uri)
    {
        using var exporter = _factory.CreateTraceExporter(options, uri);

        // The host's own provider must not see this span, and a bare listener makes the activity real.
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TestSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = TestSource.StartActivity("baget.telemetry.test", ActivityKind.Internal);
        if (activity == null) return false;

        activity.SetTag("baget.test", true);
        activity.Stop();

        using var suppress = SuppressInstrumentationScope.Begin();
        var batch = new Batch<Activity>(new[] { activity }, 1);
        return exporter.Export(batch) == ExportResult.Success;
    }
}
