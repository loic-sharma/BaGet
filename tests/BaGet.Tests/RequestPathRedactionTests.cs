using System.Diagnostics;
using BaGet.Telemetry;
using Xunit;
using BaGet.Web.V3.Logging;

namespace BaGet.Tests;

/// <summary>A credential carried in a request path (npm logout's token) is masked for the log and the trace.</summary>
public class RequestPathRedactionTests
{
    [Theory]
    [InlineData("/npm/-/user/token/SECRET", "/npm/-/user/token/Redacted")]
    [InlineData("/r/web/npm/-/user/token/SECRET", "/r/web/npm/-/user/token/Redacted")]
    [InlineData("/base/r/web/npm/-/USER/token/SECRET", "/base/r/web/npm/-/USER/token/Redacted")]
    [InlineData("/r/web/npm/-/user/token/SECRET?x=1", "/r/web/npm/-/user/token/Redacted?x=1")]
    [InlineData("/r/web/npm/-/user/token/SECRET/", "/r/web/npm/-/user/token/Redacted/")]
    [InlineData("/r/web/npm/left-pad", "/r/web/npm/left-pad")]
    [InlineData("/v3/index.json", "/v3/index.json")]
    [InlineData("", "")]
    public void TheTokenSegmentIsReplaced(string path, string expected)
        => Assert.Equal(expected, RequestPathRedaction.Redact(path));

    [Fact]
    public void ASpanPathAndUrlAreMasked()
    {
        using var source = new ActivitySource("PathRedaction");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("DELETE", ActivityKind.Server)!;
        activity.SetTag("url.path", "/r/web/npm/-/user/token/SECRET");
        activity.SetTag("url.full", "http://host/r/web/npm/-/user/token/SECRET");
        activity.SetTag("http.route", "r/{repo}/npm/-/user/token/{token}");

        new PathRedactingProcessor().OnEnd(activity);

        Assert.Equal("/r/web/npm/-/user/token/Redacted", activity.GetTagItem("url.path"));
        Assert.Equal("http://host/r/web/npm/-/user/token/Redacted", activity.GetTagItem("url.full"));
        Assert.Equal("r/{repo}/npm/-/user/token/{token}", activity.GetTagItem("http.route"));
    }
}
