using System;
using System.Collections.Generic;

namespace BaGet.Protocol.Tests;

/// <summary>
/// Parses a request URL's query string so tests can assert the parameters
/// a client sent without depending on their order.
/// </summary>
public static class TestQueryString
{
    public static IReadOnlyDictionary<string, string> Parse(Uri url)
    {
        if (url == null) throw new ArgumentNullException(nameof(url));

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var query = url.Query;

        if (query.StartsWith("?", StringComparison.Ordinal))
        {
            query = query.Substring(1);
        }

        if (query.Length == 0)
        {
            return result;
        }

        foreach (var pair in query.Split('&'))
        {
            if (pair.Length == 0) continue;

            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair.Substring(0, separator);
            var value = separator < 0 ? string.Empty : pair.Substring(separator + 1);

            result[Uri.UnescapeDataString(name)] = Uri.UnescapeDataString(value);
        }

        return result;
    }
}