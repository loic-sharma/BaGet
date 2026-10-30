using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol.Tests;

/// <summary>
/// A handler that records the requests it receives and always replies with
/// the same payload. Use this to assert the URL a client asked for, instead
/// of the response it got back.
/// </summary>
public class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly string _content;
    private readonly List<Uri?> _requests = new();

    public RecordingHttpMessageHandler(string content = "{}")
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// The URLs that were requested, in order.
    /// </summary>
    public IReadOnlyList<Uri> Requests
    {
        get
        {
            lock (_requests)
            {
                #pragma warning disable CS8619
                return _requests.ToArray();
                #pragma warning restore CS8619
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(request.RequestUri);
        }

        return Task.FromResult(new HttpResponseMessage
        {
            RequestMessage = request,
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(_content, encoding: null, mediaType: "application/json"),
        });
    }
}