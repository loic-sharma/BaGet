using System;
using System.Text.RegularExpressions;

namespace BaGet.Telemetry;

/// <summary>The telemetry signals.</summary>
public enum TelemetrySignal
{
    Traces,
    Metrics,
    Logs,
}

/// <summary>One signal's export state at an instant.</summary>
/// <param name="Signal">Which signal.</param>
/// <param name="Active">An exporter is configured (enabled and an endpoint set); otherwise nothing is exported.</param>
/// <param name="LastAttempt">The last export attempt.</param>
/// <param name="LastSuccess">The last successful export.</param>
/// <param name="FailingSince">When the current run of failures began; null while the last attempt succeeded.</param>
/// <param name="LastError">The last failure, scrubbed (no headers, credentials or URL paths/queries).</param>
/// <param name="Dropped">Items dropped because the bounded queue was full (traces only).</param>
public sealed record TelemetrySignalStatus(
    TelemetrySignal Signal,
    bool Active,
    DateTimeOffset? LastAttempt,
    DateTimeOffset? LastSuccess,
    DateTimeOffset? FailingSince,
    string? LastError,
    long Dropped);

/// <summary>
/// What the exporters last did, per signal, for the Settings page. Export is best effort and never blocks or
/// throws into a request; this is where its outcome is visible. Every error stored here has been through
/// <see cref="Scrub"/>.
/// </summary>
public sealed class TelemetryStatus
{
    private static readonly Regex Url = new(@"[a-zA-Z][a-zA-Z0-9+.-]*://[^\s'""<>]+", RegexOptions.Compiled);

    public TelemetryStatus() : this(TimeProvider.System)
    {
    }

    public TelemetryStatus(TimeProvider time)
    {
        if (time == null) throw new ArgumentNullException(nameof(time));

        Traces = new Signal(TelemetrySignal.Traces, time);
        Metrics = new Signal(TelemetrySignal.Metrics, time);
        Logs = new Signal(TelemetrySignal.Logs, time);
    }

    internal Signal Traces { get; }
    internal Signal Metrics { get; }
    internal Signal Logs { get; }

    internal Signal For(TelemetrySignal signal) => signal switch
    {
        TelemetrySignal.Traces => Traces,
        TelemetrySignal.Metrics => Metrics,
        _ => Logs,
    };

    /// <summary>The state of one signal.</summary>
    public TelemetrySignalStatus Get(TelemetrySignal signal) => For(signal).Snapshot();

    /// <summary>The state of every signal.</summary>
    public TelemetrySignalStatus[] GetAll()
        => new[] { Get(TelemetrySignal.Traces), Get(TelemetrySignal.Metrics), Get(TelemetrySignal.Logs) };

    /// <summary>
    /// Make a message safe to show: each URL is cut to scheme, host and port (userinfo, path and query can hold
    /// tokens), and every secret (an OTLP header value, or the whole <c>key=value</c> list) is replaced.
    /// </summary>
    public static string Scrub(string? message, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;

        var result = message!;
        foreach (var secret in secrets)
        {
            if (string.IsNullOrWhiteSpace(secret)) continue;

            result = result.Replace(secret!, "***", StringComparison.Ordinal);
            foreach (var pair in secret!.Split(','))
            {
                var index = pair.IndexOf('=');
                var value = (index >= 0 ? pair.Substring(index + 1) : pair).Trim();
                if (value.Length >= 3) result = result.Replace(value, "***", StringComparison.Ordinal);
            }
        }

        return Url.Replace(result, match =>
            Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
                ? uri.Scheme + "://" + uri.Authority.Substring(uri.Authority.LastIndexOf('@') + 1)
                : "<url>");
    }

    /// <summary>A scrubbed one-line description of an exception: its type and message, never a stack.</summary>
    public static string Describe(Exception exception, params string?[] secrets)
    {
        var inner = exception;
        while (inner.InnerException != null && string.IsNullOrWhiteSpace(inner.Message)) inner = inner.InnerException;
        return Scrub(inner.GetType().Name + ": " + inner.Message, secrets);
    }

    /// <summary>The mutable state of one signal; written by the exporters.</summary>
    internal sealed class Signal
    {
        private readonly object _lock = new();
        private readonly TimeProvider _time;
        private readonly TelemetrySignal _signal;
        private bool _active;
        private DateTimeOffset? _lastAttempt;
        private DateTimeOffset? _lastSuccess;
        private DateTimeOffset? _failingSince;
        private string? _lastError;
        private Func<long>? _dropped;

        public Signal(TelemetrySignal signal, TimeProvider time)
        {
            _signal = signal;
            _time = time;
        }

        /// <summary>The exporter was (re)configured: a new endpoint starts a fresh history.</summary>
        public void Configured(bool active, Func<long>? dropped = null)
        {
            lock (_lock)
            {
                _active = active;
                _lastAttempt = null;
                _lastSuccess = null;
                _failingSince = null;
                _lastError = null;
                if (dropped != null) _dropped = dropped;
            }
        }

        public void Success()
        {
            var now = _time.GetUtcNow();
            lock (_lock)
            {
                _lastAttempt = now;
                _lastSuccess = now;
                _failingSince = null;
                _lastError = null;
            }
        }

        /// <param name="scrubbedError">Already scrubbed.</param>
        public void Failure(string scrubbedError)
        {
            var now = _time.GetUtcNow();
            lock (_lock)
            {
                _lastAttempt = now;
                _failingSince ??= now;
                _lastError = scrubbedError;
            }
        }

        public TelemetrySignalStatus Snapshot()
        {
            long dropped = 0;
            Func<long>? provider;
            lock (_lock) provider = _dropped;
            if (provider != null)
            {
                try
                {
                    dropped = provider();
                }
                catch
                {
                    // The processor may be disposed already.
                }
            }

            lock (_lock)
            {
                return new TelemetrySignalStatus(_signal, _active, _lastAttempt, _lastSuccess, _failingSince, _lastError, dropped);
            }
        }
    }
}
