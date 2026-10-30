using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BaGet.Core;

/// <summary>
/// BaGet's own traces and metrics, emitted with <c>System.Diagnostics</c> only (no OpenTelemetry dependency in the
/// libraries). With no listener attached <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> returns
/// null and counters do nothing, so instrumentation costs nothing until the host subscribes to <see cref="Name"/>.
/// </summary>
/// <remarks>
/// Tags carry package ids and versions, results, table names and counts. Never put API keys, connection strings,
/// user names or email addresses in a tag.
/// </remarks>
public static class BaGetDiagnostics
{
    /// <summary>The name of both the <see cref="Source"/> and the <see cref="Meter"/>.</summary>
    public const string Name = "BaGet";

    /// <summary>Span names.</summary>
    public static class Spans
    {
        public const string PackageIndex = "package.index";
        public const string PackageValidate = "package.validate";
        public const string MirrorFetch = "mirror.fetch";
        public const string DatabaseCopy = "database.copy";
        public const string DatabaseCopyTable = "database.copy.table";
    }

    /// <summary>Tag names.</summary>
    public static class Tags
    {
        public const string PackageId = "baget.package.id";
        public const string PackageVersion = "baget.package.version";
        public const string Result = "baget.result";
        public const string SubmissionId = "baget.validation.submission";
        public const string Validator = "baget.validation.validator";
        public const string Table = "baget.copy.table";
        public const string Rows = "baget.copy.rows";
        public const string Tables = "baget.copy.tables";
        public const string SourceType = "baget.copy.source";
        public const string TargetType = "baget.copy.target";
    }

    /// <summary>The source of BaGet's spans.</summary>
    public static readonly ActivitySource Source = new(Name);

    /// <summary>The meter of BaGet's counters.</summary>
    public static readonly Meter Meter = new(Name);

    /// <summary>Packages accepted by a push, tagged by <see cref="Tags.Result"/>.</summary>
    public static readonly Counter<long> PackagesPushed
        = Meter.CreateCounter<long>("baget.packages.pushed", "{package}", "Packages indexed, by result.");

    /// <summary>Package downloads recorded.</summary>
    public static readonly Counter<long> Downloads
        = Meter.CreateCounter<long>("baget.downloads", "{download}", "Package downloads recorded.");

    /// <summary>Finished validation submissions, tagged by <see cref="Tags.Result"/>.</summary>
    public static readonly Counter<long> ValidationResults
        = Meter.CreateCounter<long>("baget.validation.results", "{submission}", "Finished validation submissions, by result.");

    /// <summary>Marks an activity as failed without recording the exception message (it may hold paths or connection details).</summary>
    public static void Fail(Activity? activity, System.Exception exception)
    {
        if (activity == null) return;

        activity.SetTag(Tags.Result, "error");
        activity.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
    }
}
