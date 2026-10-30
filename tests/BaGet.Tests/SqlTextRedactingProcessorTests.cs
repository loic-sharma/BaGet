using System.Diagnostics;
using BaGet.Telemetry;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// <see cref="SqlTextRedactingProcessor"/> driven directly with spans shaped like those of the EF instrumentation under
/// each semantic-convention mode (<c>OTEL_SEMCONV_STABILITY_OPT_IN</c> unset, <c>database</c>, <c>database/dup</c>).
/// </summary>
public class SqlTextRedactingProcessorTests
{
    private const string Sql = "SELECT [p].[Id] FROM [Packages] AS [p]";

    private static Activity Span(string displayName, params (string Key, string Value)[] tags)
    {
        using var source = new ActivitySource("SqlTextRedactingProcessorTests");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var activity = source.StartActivity(displayName, ActivityKind.Client)!;
        foreach (var (key, value) in tags) activity.SetTag(key, value);
        return activity;
    }

    [Fact]
    public void TheQuerySummaryTagIsRemovedAndASpanNamedFromItGetsANonSqlName()
    {
        using var activity = Span("SELECT Packages", ("db.query.summary", "SELECT Packages"), ("db.query.text", Sql), ("db.operation.name", "SELECT"), ("db.namespace", "baget"), ("db.system.name", "sqlite"));

        new SqlTextRedactingProcessor().OnEnd(activity);

        Assert.Null(activity.GetTagItem("db.query.summary"));
        Assert.Null(activity.GetTagItem("db.query.text"));
        Assert.Equal("SELECT baget", activity.DisplayName);
    }

    [Fact]
    public void ASpanNamedFromTheQueryTextFallsBackToTheSystemName()
    {
        using var activity = Span(Sql, ("db.statement", Sql), ("db.system", "sqlite"));

        new SqlTextRedactingProcessor().OnEnd(activity);

        Assert.Null(activity.GetTagItem("db.statement"));
        Assert.Equal("sqlite", activity.DisplayName);
        Assert.DoesNotContain("SELECT", activity.DisplayName);
    }

    [Fact]
    public void ASpanNameThatIsNotSqlIsKept()
    {
        using var activity = Span("baget", ("db.query.text", Sql), ("db.system", "sqlite"));

        new SqlTextRedactingProcessor().OnEnd(activity);

        Assert.Null(activity.GetTagItem("db.query.text"));
        Assert.Equal("baget", activity.DisplayName);
    }
}
