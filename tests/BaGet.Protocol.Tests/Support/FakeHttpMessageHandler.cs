using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol.Tests;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that serves responses configured per URL and records
/// the requests it received. <see cref="TestDataHttpMessageHandler"/> serves fixed test data;
/// this one lets a test make an endpoint fail, change its answer, or assert that an endpoint
/// was never requested.
///
/// Query strings are ignored when matching, as the clients add their own.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _responses
        = new(StringComparer.Ordinal);

    private readonly List<string> _requests = new();
    private readonly object _lock = new();

    /// <summary>
    /// The URLs that were requested, without their query strings, in order.
    /// </summary>
    public IReadOnlyList<string> Requests
    {
        get { lock (_lock) { return _requests.ToList(); } }
    }

    /// <summary>
    /// How many times a URL was requested.
    /// </summary>
    public int RequestCount(string url) => Requests.Count(request => request == Key(url));

    /// <summary>
    /// Serve JSON content for a URL.
    /// </summary>
    public FakeHttpMessageHandler Json(string url, string content) => Json(url, () => content);

    /// <summary>
    /// Serve JSON content for a URL. The content is resolved on each request, so a test can
    /// change what an endpoint answers.
    /// </summary>
    public FakeHttpMessageHandler Json(string url, Func<string> content)
    {
        return Respond(url, request => new HttpResponseMessage
        {
            RequestMessage = request,
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(content(), encoding: null, mediaType: "application/json"),
        });
    }

    /// <summary>
    /// Answer a URL with a status code and no content. The server answered: this is not a
    /// transport failure.
    /// </summary>
    public FakeHttpMessageHandler Status(string url, HttpStatusCode statusCode)
    {
        return Respond(url, request => new HttpResponseMessage
        {
            RequestMessage = request,
            StatusCode = statusCode,
            Content = new StringContent(string.Empty),
        });
    }

    /// <summary>
    /// Fail a URL at the transport level, the way a refused connection, a DNS failure or a
    /// dropped response does: an <see cref="HttpRequestException"/> with no status code.
    /// </summary>
    public FakeHttpMessageHandler Unreachable(string url)
    {
        return Throws(url, () => new HttpRequestException($"Connection refused by {url}"));
    }

    /// <summary>
    /// Fail a URL the way <see cref="HttpClient"/> reports its own timeout: a cancellation
    /// that the caller did not ask for.
    /// </summary>
    public FakeHttpMessageHandler TimesOut(string url)
    {
        return Throws(url, () => new TaskCanceledException(
            $"The request to {url} timed out",
            new TimeoutException()));
    }

    /// <summary>
    /// Throw when a URL is requested.
    /// </summary>
    public FakeHttpMessageHandler Throws(string url, Func<Exception> exception)
    {
        return Respond(url, _ => throw exception());
    }

    private FakeHttpMessageHandler Respond(string url, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses[Key(url)] = respond;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = Key(request.RequestUri);

        lock (_lock)
        {
            _requests.Add(key);
        }

        if (request.Method != HttpMethod.Get || !_responses.TryGetValue(key, out var respond))
        {
            return Task.FromResult(new HttpResponseMessage
            {
                RequestMessage = request,
                StatusCode = HttpStatusCode.NotFound,
                Content = new StringContent(string.Empty),
            });
        }

        return Task.FromResult(respond(request));
    }

    private static string Key(string url) => Key(new Uri(url));

    #pragma warning disable CS8602
    private static string Key(Uri? url) => url.GetLeftPart(UriPartial.Path).TrimEnd('/');
    #pragma warning restore CS8602
}