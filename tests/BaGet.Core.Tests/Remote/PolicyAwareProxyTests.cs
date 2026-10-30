using System;
using System.Net;
using Xunit;

namespace BaGet.Core.Tests.Remote;

public class PolicyAwareProxyTests
{
    [Fact]
    public void WhileBlockingEveryRequestIsDirect()
    {
        var proxy = new PolicyAwareProxy(() => true);
        var target = new Uri("http://example.test/x");

        Assert.True(proxy.IsBypassed(target));
        Assert.Equal(target, proxy.GetProxy(target));
    }

    [Fact]
    public void WhenNotBlockingTheSystemProxyDecides()
    {
        var proxy = new PolicyAwareProxy(() => false);
        var target = new Uri("http://example.test/x");
        var system = HttpClient_DefaultProxy();

        Assert.Equal(system.IsBypassed(target), proxy.IsBypassed(target));
        Assert.Equal(system.GetProxy(target), proxy.GetProxy(target));
    }

    [Fact]
    public void TheFlagIsReadPerRequest()
    {
        var blocking = false;
        var proxy = new PolicyAwareProxy(() => blocking);
        var target = new Uri("http://example.test/x");

        blocking = true;
        Assert.True(proxy.IsBypassed(target));
        Assert.Equal(target, proxy.GetProxy(target));
    }

    private static IWebProxy HttpClient_DefaultProxy() => System.Net.Http.HttpClient.DefaultProxy;
}
