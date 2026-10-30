using System.Diagnostics;
using OpenTelemetry;
using BaGet.Web.V3.Logging;

namespace BaGet.Telemetry;

/// <summary>
/// Masks credentials that a route carries in its path (<see cref="RequestPathRedaction"/>) in the tags of finished spans:
/// the ASP.NET Core instrumentation records the request path as <c>url.path</c> (<c>http.target</c> under the older
/// conventions), which for npm's logout holds the API key being revoked. It must be added before the exporting processor.
/// </summary>
internal sealed class PathRedactingProcessor : BaseProcessor<Activity>
{
    /// <summary>The tags that hold a request path or URL.</summary>
    internal static readonly string[] Tags = { "url.path", "http.target", "url.full", "http.url" };

    public override void OnEnd(Activity data)
    {
        foreach (var tag in Tags)
        {
            if (data.GetTagItem(tag) is not string value) continue;

            var redacted = RequestPathRedaction.Redact(value);
            if (!ReferenceEquals(redacted, value) && redacted != value) data.SetTag(tag, redacted);
        }
    }
}
