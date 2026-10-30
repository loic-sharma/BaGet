using System;
using System.Net;
using System.Net.Http;

namespace BaGet.Core;

/// <summary>
/// The system proxy, except while <c>Remote:BlockPrivateAddresses</c> is on: a connection through a proxy reaches the proxy's address, so the
/// policy could not see (or refuse) the target's. With the policy on every request is made directly.
/// </summary>
internal sealed class PolicyAwareProxy : IWebProxy
{
    private readonly Func<bool> _blocking;
    private readonly IWebProxy _system = HttpClient.DefaultProxy;

    public PolicyAwareProxy(Func<bool> blocking)
    {
        _blocking = blocking ?? throw new ArgumentNullException(nameof(blocking));
    }

    public ICredentials? Credentials
    {
        get => _system.Credentials;
        set => _system.Credentials = value;
    }

    public Uri? GetProxy(Uri destination) => _blocking() ? destination : _system.GetProxy(destination);

    public bool IsBypassed(Uri host) => _blocking() || _system.IsBypassed(host);
}
