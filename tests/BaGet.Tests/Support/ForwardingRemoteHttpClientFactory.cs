using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;

namespace BaGet.Tests;

/// <summary>A request an upstream received.</summary>
/// <param name="Uri">The URL asked.</param>
/// <param name="Authorization">The Authorization header the upstream saw.</param>
internal sealed record UpstreamRequest(Uri Uri, string? Authorization);

/// <summary>
/// The <see cref="IRemoteHttpClientFactory"/> of the tests: every request a remote repository (or the mirror) sends
/// goes to an in-memory upstream through <c>upstream</c>, whatever the configured host. The factory's own
/// credential handler still runs in front, so what an upstream would receive can be asserted. It records the requests
/// and can pretend the upstream is down.
/// </summary>
internal sealed class ForwardingRemoteHttpClientFactory : RemoteHttpClientFactory
{
    private readonly HttpClient _upstream;
    private readonly ConcurrentQueue<UpstreamRequest> _requests = new();

    public ForwardingRemoteHttpClientFactory(HttpClient upstream)
    {
        _upstream = upstream;
    }

    /// <summary>While true every request fails as if the upstream could not be reached.</summary>
    public bool Down
    {
        get => _down;
        set => _down = value;
    }

    private volatile bool _down;

    /// <summary>Every request the upstream received (those sent while <see cref="Down"/> excluded).</summary>
    public UpstreamRequest[] Requests => _requests.ToArray();

    /// <summary>How many requests whose path and query contain <paramref name="fragment"/> the upstream received.</summary>
    public int Count(string fragment)
        => _requests.Count(r => r.Uri.PathAndQuery.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    protected override HttpMessageHandler CreateInnerHandler() => new Forward(this);

    private sealed class Forward : HttpMessageHandler
    {
        private readonly ForwardingRemoteHttpClientFactory _owner;

        public Forward(ForwardingRemoteHttpClientFactory owner)
        {
            _owner = owner;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_owner.Down) throw new HttpRequestException("The upstream is down.");

            _owner._requests.Enqueue(new UpstreamRequest(request.RequestUri!, request.Headers.Authorization?.ToString()));

            // A request message can be sent once; the upstream client needs one of its own.
            var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
            foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);

            return _owner._upstream.SendAsync(copy, cancellationToken);
        }
    }
}

/// <summary>A clock the test moves.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
