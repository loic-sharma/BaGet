using System;

namespace BaGet.Web.V3.Logging;

/// <summary>
/// Hides the secrets that a protocol puts in the path of a request, before the path reaches a log event (request line or hosting-scope property: <c>RequestPathRedactingEnricher</c>) or a trace. The query
/// string and headers never reach either (see the request log template and the telemetry setup); a path would, so the one
/// route that carries a credential in it is masked here: npm's <c>DELETE -/user/token/{token}</c> (<c>npm logout</c>) names
/// the API key to revoke as its last segment. A route that gets a secret in its path must be added here.
/// </summary>
public static class RequestPathRedaction
{
    /// <summary>What replaces a secret segment.</summary>
    public const string Redacted = "Redacted";

    private const string TokenSegments = "/-/user/token/";

    /// <summary>
    /// The path with the secret segments replaced by <see cref="Redacted"/>; the path itself when it carries none. The match
    /// is case-insensitive and does not depend on the repository prefix (<c>/r/{repo}/npm/</c>) or the path base.
    /// </summary>
    /// <param name="path">A request path, or any string that holds one (<c>url.path</c>, <c>http.target</c>).</param>
    public static string? Redact(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        var index = path.IndexOf(TokenSegments, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return path;

        var start = index + TokenSegments.Length;
        var end = path.IndexOfAny(new[] { '/', '?', '#' }, start);

        return path.Substring(0, start) + Redacted + (end < 0 ? string.Empty : path.Substring(end));
    }
}
