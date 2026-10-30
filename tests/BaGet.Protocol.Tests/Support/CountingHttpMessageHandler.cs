using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol.Tests;

/// <summary>
/// Records the requests that pass through to an inner handler, so tests can
/// assert how many times a resource was fetched.
/// </summary>
public class CountingHttpMessageHandler : DelegatingHandler
{
    private readonly List<Uri?> _requests = new();

    public CountingHttpMessageHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

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

    public int RequestCount(string url)
    {
        return Requests.Count(u => u.AbsoluteUri == url);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(request.RequestUri);
        }

        return base.SendAsync(request, cancellationToken);
    }
}