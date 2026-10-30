using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using OpenTelemetry;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace BaGet.Telemetry;

/// <summary>
/// Builds one <c>Serilog.Sinks.OpenTelemetry</c> sink for the current options. The sink batches (bounded queue, drops
/// when full), records <c>TraceId</c> / <c>SpanId</c> from <c>Activity.Current</c> on every event, and its HTTP traffic
/// goes through <see cref="ExportStatusHandler"/> so the outcome reaches <see cref="TelemetryStatus"/>.
/// </summary>
internal static class OpenTelemetryLogSink
{
    public static ILogEventSink Create(TelemetryOptions options, Uri endpoint, string serviceName, TelemetryStatus.Signal status)
    {
        var timeout = options.Otlp?.Timeout ?? TimeSpan.FromSeconds(10);
        var handler = new ExportStatusHandler(new SocketsHttpHandler(), status, timeout, options.Otlp?.Headers);

        try
        {
            var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.OpenTelemetry(sink =>
                {
                    sink.Endpoint = endpoint.ToString();
                    sink.Protocol = TelemetryEndpoints.IsGrpc(options) ? OtlpProtocol.Grpc : OtlpProtocol.HttpProtobuf;
                    sink.Headers = TelemetryEndpoints.ParseHeaders(options.Otlp?.Headers);
                    sink.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = serviceName };
                    sink.HttpMessageHandler = handler;
                    sink.IncludedData = IncludedData.TraceIdField | IncludedData.SpanIdField | IncludedData.MessageTemplateTextAttribute
                        | IncludedData.SourceContextAttribute;
                    // The export itself must not be traced (it would trace its own export).
                    sink.OnBeginSuppressInstrumentation = SuppressInstrumentationScope.Begin;
                    sink.BatchingOptions.BatchSizeLimit = 512;
                    sink.BatchingOptions.BufferingTimeLimit = TimeSpan.FromSeconds(2);
                    sink.BatchingOptions.QueueLimit = 10_000;
                })
                .CreateLogger();

            return new OwnedSink(logger, logger, handler);
        }
        catch
        {
            handler.Dispose();
            throw;
        }
    }

    /// <summary>A sink and what it must dispose with it (disposing the logger flushes its batch).</summary>
    private sealed class OwnedSink : ILogEventSink, IDisposable
    {
        private readonly ILogEventSink _sink;
        private readonly IDisposable[] _owned;
        private int _disposed;

        public OwnedSink(ILogEventSink sink, params IDisposable[] owned)
        {
            _sink = sink;
            _owned = owned;
        }

        public void Emit(LogEvent logEvent) => _sink.Emit(logEvent);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            foreach (var owned in _owned)
            {
                try
                {
                    owned.Dispose();
                }
                catch
                {
                    // Best effort.
                }
            }
        }
    }
}

/// <summary>
/// Sits under an OTLP HTTP client: bounds each request by the export timeout and records the result in the
/// <see cref="TelemetryStatus"/>. A gRPC reply is a success unless it is a trailers-only error (<c>grpc-status</c> other
/// than 0 in the headers, for example Jaeger's <c>Unimplemented</c> for logs); the headers' values are never recorded.
/// </summary>
internal sealed class ExportStatusHandler : DelegatingHandler
{
    private readonly TelemetryStatus.Signal _status;
    private readonly TimeSpan _timeout;
    private readonly string? _headers;

    public ExportStatusHandler(HttpMessageHandler inner, TelemetryStatus.Signal status, TimeSpan timeout, string? headers)
        : base(inner)
    {
        _status = status;
        _timeout = timeout;
        _headers = headers;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            var response = await base.SendAsync(request, timeout.Token).ConfigureAwait(false);

            string? grpcStatus = null;
            if (response.Headers.TryGetValues("grpc-status", out var values)) grpcStatus = values.FirstOrDefault();

            if (response.IsSuccessStatusCode && (grpcStatus == null || grpcStatus == "0"))
            {
                _status.Success();
            }
            else
            {
                _status.Failure(grpcStatus != null && grpcStatus != "0"
                    ? "The collector refused the export (gRPC status " + grpcStatus + ")."
                    : "The collector answered HTTP " + (int)response.StatusCode + ".");
            }

            return response;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _status.Failure("The export timed out after " + _timeout.TotalSeconds.ToString("0.#") + " s.");
            throw;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status.Failure(TelemetryStatus.Describe(e, _headers));
            throw;
        }
    }
}
