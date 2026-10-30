using System;
using System.Linq;
using Serilog.Core;
using Serilog.Events;
using BaGet.Web.V3.Logging;

namespace BaGet;

/// <summary>
/// Masks the credentials a route carries in its path (<see cref="RequestPathRedaction"/>) in every log event, whichever
/// property holds the path: ASP.NET Core's hosting scope puts the raw <c>RequestPath</c> on every event written during a
/// request and Serilog.Extensions.Logging copies scope properties onto the event, so the sinks (file, console, the OTLP log
/// export) would otherwise see npm logout's live API key. It rewrites every string property (the rule only changes text that
/// holds a secret segment), so a path under another name (<c>Path</c>, <c>RequestUri</c>) is covered too. Registered after
/// <c>Enrich.FromLogContext()</c> in <c>ConfigureLogging</c>.
/// </summary>
internal sealed class RequestPathRedactingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Snapshot: AddOrUpdateProperty changes the collection being enumerated.
        foreach (var property in logEvent.Properties.ToArray())
        {
            if (property.Value is not ScalarValue { Value: string text }) continue;

            var redacted = RequestPathRedaction.Redact(text);
            if (!string.Equals(redacted, text, StringComparison.Ordinal))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, new ScalarValue(redacted)));
            }
        }
    }
}
