using System.Diagnostics;
using System.Linq;
using OpenTelemetry;

namespace BaGet.Telemetry;

/// <summary>
/// Removes the SQL text from finished spans. <c>OpenTelemetry.Instrumentation.EntityFrameworkCore</c> offers no switch
/// for it and always records the (literal-sanitized) command as <c>db.query.text</c> (<c>db.statement</c> under the
/// older conventions, and <c>db.query.summary</c> under <c>OTEL_SEMCONV_STABILITY_OPT_IN=database</c> or
/// <c>database/dup</c>, which also uses it as the span name): table and column names and the query shape do not belong
/// in a trace backend by default. A span name taken from one of those values is replaced by a non-SQL one, following the
/// OpenTelemetry convention <c>{db.operation.name} {db.namespace}</c> (else the system name). It must be added before
/// the exporting processor so the tags are gone when the batch is queued.
/// </summary>
internal sealed class SqlTextRedactingProcessor : BaseProcessor<Activity>
{
    /// <summary>The tags that hold SQL text.</summary>
    internal static readonly string[] Tags = { "db.query.text", "db.statement", "db.query.summary" };

    public override void OnEnd(Activity data)
    {
        var nameFromSql = false;
        foreach (var tag in Tags)
        {
            var value = data.GetTagItem(tag) as string;
            if (value == null) continue;

            if (value.Length > 0 && data.DisplayName.Contains(value, System.StringComparison.Ordinal)) nameFromSql = true;

            // Setting a null value removes the tag.
            data.SetTag(tag, null);
        }

        // A tag set to a non-string value is removed too.
        foreach (var tag in Tags)
        {
            if (data.GetTagItem(tag) != null) data.SetTag(tag, null);
        }

        if (nameFromSql) data.DisplayName = NeutralName(data);
    }

    private static string NeutralName(Activity data)
    {
        var operation = (data.GetTagItem("db.operation.name") ?? data.GetTagItem("db.operation")) as string;
        var database = (data.GetTagItem("db.namespace") ?? data.GetTagItem("db.name")) as string;
        var system = (data.GetTagItem("db.system.name") ?? data.GetTagItem("db.system")) as string;

        var name = string.Join(" ", new[] { operation, database }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (name.Length > 0) return name;

        return string.IsNullOrWhiteSpace(system) ? "db" : system!;
    }
}
