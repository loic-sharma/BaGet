using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>
/// Creates the <see cref="HttpClient"/> that talks to a remote repository's upstream. Replace it (a test, a host
/// with its own proxy or certificate rules) by registering another implementation.
/// </summary>
public interface IRemoteHttpClientFactory
{
    /// <summary>
    /// A client for <paramref name="settings"/>: the download timeout applied and, when credentials are configured,
    /// HTTP Basic sent to the upstream's own host only (never to a host a redirect or a service index points to).
    /// </summary>
    HttpClient Create(RemoteRepositorySettings settings);

    /// <summary>
    /// Like <see cref="Create"/> for a one-off probe (Test connection): the client must not follow redirects itself, so
    /// the caller decides whether to follow one. The default returns <see cref="Create"/>.
    /// </summary>
    HttpClient CreateProbe(RemoteRepositorySettings settings) => Create(settings);
}

/// <summary>
/// The default <see cref="IRemoteHttpClientFactory"/>.
/// </summary>
public class RemoteHttpClientFactory : IRemoteHttpClientFactory
{
    private readonly IOptionsMonitor<RemoteOptions>? _options;

    /// <summary>A factory that connects to any address.</summary>
    public RemoteHttpClientFactory()
    {
    }

    /// <param name="options">The <c>Remote:*</c> options; with <see cref="RemoteOptions.BlockPrivateAddresses"/> on every connection to a non-public address is refused (read on each connection, so the setting is live).</param>
    public RemoteHttpClientFactory(IOptionsMonitor<RemoteOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc/>
    public HttpClient Create(RemoteRepositorySettings settings) => Build(settings, followRedirects: true);

    /// <inheritdoc/>
    public HttpClient CreateProbe(RemoteRepositorySettings settings) => Build(settings, followRedirects: false);

    private HttpClient Build(RemoteRepositorySettings settings, bool followRedirects)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        var handler = CreateInnerHandler();
        if (!followRedirects && handler is SocketsHttpHandler sockets) sockets.AllowAutoRedirect = false;
        // A Docker upstream authenticates per request (a challenge, then a token from the realm it names, or Basic): its client
        // sends the credentials itself, to the right place, so no handler adds them to every request.
        if (settings.HasCredentials && settings.UrlOrNull != null && settings.Protocol != RemoteProtocol.Docker)
        {
            // An npm upstream with only a password is given a token (npm's _authToken), which is a bearer credential.
            var bearer = settings.Protocol == RemoteProtocol.Npm && string.IsNullOrEmpty(settings.Username);
            handler = new HostScopedBasicAuthHandler(settings.UrlOrNull, settings.Username, settings.Password, bearer) { InnerHandler = handler };
        }

        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(settings.DownloadTimeoutSeconds),
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"{assembly?.GetName().Name ?? "BaGet"}/{version}");

        return client;
    }

    /// <summary>The handler that sends the requests; tests replace it with an in-memory one.</summary>
    protected virtual HttpMessageHandler CreateInnerHandler()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        if (_options != null)
        {
            handler.ConnectCallback = ConnectAsync;

            // Through a proxy the connect callback would see the proxy's address, not the target's: while the policy is on
            // (read per request, so the setting stays live) the system proxy is not used and the target is connected to directly.
            handler.Proxy = new PolicyAwareProxy(() => _options.CurrentValue.BlockPrivateAddresses);
        }

        return handler;
    }

    // The ordinary TCP connect, refusing a non-public address while Remote:BlockPrivateAddresses is on. It checks the
    // addresses actually connected to, so a name that resolves differently later, or a redirect, cannot get around it.
    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);

        if (_options!.CurrentValue.BlockPrivateAddresses
            && (addresses.Any(RemoteAddressPolicy.IsBlocked)
                || endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            throw new PrivateAddressException();
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken).ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }
}

/// <summary>
/// Adds HTTP Basic (or, for a token, Bearer) credentials to requests for one origin (scheme, host, port) and to no other.
/// </summary>
internal sealed class HostScopedBasicAuthHandler : DelegatingHandler
{
    private readonly Uri _origin;
    private readonly AuthenticationHeaderValue _header;

    public HostScopedBasicAuthHandler(Uri origin, string? username, string? password, bool bearer = false)
    {
        _origin = origin;
        _header = bearer
            ? new AuthenticationHeaderValue("Bearer", password ?? string.Empty)
            : new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes((username ?? string.Empty) + ":" + (password ?? string.Empty))));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (request.Headers.Authorization == null
            && uri != null
            && uri.IsAbsoluteUri
            && Uri.Compare(uri, _origin, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
        {
            request.Headers.Authorization = _header;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
