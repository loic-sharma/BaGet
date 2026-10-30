using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Core.Tests.Remote;

public class RemoteRepositorySettingsTests
{
    private static RemoteRepositorySettings Valid() => new() { Url = "https://api.nuget.org/v3/index.json" };

    [Fact]
    public void DefaultsAreUsable()
    {
        var settings = Valid();

        Assert.Empty(settings.Validate());
        Assert.Equal(RemoteProtocol.V3, settings.Protocol);
        Assert.True(settings.StoreLocally);
        Assert.False(settings.Offline);
        Assert.Equal(600, settings.MetadataTtlSeconds);
        Assert.Equal(60, settings.NegativeTtlSeconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not a url")]
    [InlineData("ftp://host/index.json")]
    [InlineData("/relative/index.json")]
    [InlineData("https://user:pass@host/index.json")]
    public void AnUnusableUrlIsReported(string? url)
    {
        var settings = Valid();
        settings.Url = url;

        Assert.NotEmpty(settings.Validate());
    }

    [Fact]
    public void AV2UrlWithAQueryIsReportedButAV3OneIsNot()
    {
        var v2 = Valid();
        v2.Protocol = RemoteProtocol.V2;
        v2.Url = "https://host/nuget?x=1";
        Assert.Contains(v2.Validate(), p => p.Contains("query"));

        var v3 = Valid();
        v3.Url = "https://host/index.json?x=1";
        Assert.Empty(v3.Validate());
    }

    [Theory]
    [InlineData(-1, 60, 600)]
    [InlineData(600, -1, 600)]
    [InlineData(600, 60, 0)]
    [InlineData(RemoteRepositorySettings.MaxTtlSeconds + 1, 60, 600)]
    public void OutOfRangePeriodsAreReported(int metadata, int negative, int timeout)
    {
        var settings = Valid();
        settings.MetadataTtlSeconds = metadata;
        settings.NegativeTtlSeconds = negative;
        settings.DownloadTimeoutSeconds = timeout;

        Assert.NotEmpty(settings.Validate());
    }

    [Fact]
    public void AColonInTheUserNameIsReportedBecauseBasicCannotCarryIt()
    {
        var settings = Valid();
        settings.Username = "a:b";

        Assert.Contains(settings.Validate(), p => p.Contains("':'"));
    }

    [Fact]
    public void MirrorOptionsBecomeSettingsThatAskEveryTime()
    {
        var settings = RemoteRepositorySettings.FromMirror(new MirrorOptions
        {
            Enabled = true,
            PackageSource = new Uri("https://example.org/v2/"),
            Legacy = true,
            PackageDownloadTimeoutSeconds = 42,
        });

        Assert.Equal("https://example.org/v2/", settings.Url);
        Assert.Equal(RemoteProtocol.V2, settings.Protocol);
        Assert.Equal(0, settings.MetadataTtlSeconds);
        Assert.Equal(0, settings.NegativeTtlSeconds);
        Assert.Equal(42, settings.DownloadTimeoutSeconds);
        Assert.True(settings.StoreLocally);
    }

    [Fact]
    public void TheTransportKeyChangesWithWhatAffectsTheConnectionAndHidesThePassword()
    {
        var a = Valid();
        a.Username = "u";
        a.Password = "super-secret";
        var key = a.TransportKey();

        Assert.DoesNotContain("super-secret", key);

        var b = a.Clone();
        b.MetadataTtlSeconds = 5;
        b.Offline = true;
        Assert.Equal(key, b.TransportKey());

        b.Password = "other";
        Assert.NotEqual(key, b.TransportKey());
        b = a.Clone();
        b.Url = "https://elsewhere/index.json";
        Assert.NotEqual(key, b.TransportKey());
    }
}

public class RemoteSettingsCodecTests
{
    private sealed class ReversingProtector : IRemoteCredentialProtector
    {
        private static string Flip(string value)
        {
            var chars = value.ToCharArray();
            Array.Reverse(chars);

            return new string(chars);
        }

        public string Protect(string secret) => "p:" + Flip(secret);

        public string? TryUnprotect(string protectedValue)
            => protectedValue.StartsWith("p:") ? Flip(protectedValue.Substring(2)) : null;
    }

    [Fact]
    public void ThePasswordIsStoredProtectedAndReadBack()
    {
        var codec = new RemoteSettingsCodec(new ReversingProtector());
        var json = codec.Serialize(new RemoteRepositorySettings { Url = "https://h/i.json", Username = "bob", Password = "hunter2" });

        Assert.DoesNotContain("hunter2", json);
        Assert.Contains("protectedPassword", json);

        var back = codec.Deserialize(json)!;
        Assert.Equal("hunter2", back.Password);
        Assert.Equal("bob", back.Username);
        Assert.Equal("https://h/i.json", back.Url);
    }

    [Fact]
    public void AStoredProtectedPasswordIsKeptWhenNoNewOneIsGiven()
    {
        var codec = new RemoteSettingsCodec(new ReversingProtector());
        var stored = codec.Deserialize(codec.Serialize(new RemoteRepositorySettings { Url = "https://h/i.json", Password = "pw" }))!;

        var again = codec.Deserialize(codec.Serialize(new RemoteRepositorySettings
        {
            Url = "https://h/i.json",
            ProtectedPassword = stored.ProtectedPassword,
        }))!;

        Assert.Equal("pw", again.Password);
    }

    [Fact]
    public void WithoutAProtectorAPasswordIsRefusedButSettingsWithoutOneAreStored()
    {
        var codec = new RemoteSettingsCodec();

        Assert.Throws<InvalidOperationException>(() => codec.Serialize(new RemoteRepositorySettings { Url = "https://h/i.json", Password = "pw" }));
        Assert.Null(codec.Deserialize(codec.Serialize(new RemoteRepositorySettings { Url = "https://h/i.json" }))!.Password);
    }

    [Fact]
    public void APasswordThatCannotBeUnprotectedReadsAsAbsentInsteadOfFailing()
    {
        var codec = new RemoteSettingsCodec(new ReversingProtector());

        var back = codec.Deserialize("{\"url\":\"https://h/i.json\",\"protectedPassword\":\"garbage\"}")!;

        Assert.Null(back.Password);
        Assert.Equal("https://h/i.json", back.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void UnreadableJsonIsNull(string? json)
    {
        Assert.Null(new RemoteSettingsCodec().Deserialize(json));
    }

    [Fact]
    public void ProtocolIsStoredByName()
    {
        var json = new RemoteSettingsCodec().Serialize(new RemoteRepositorySettings { Url = "https://h/i.json", Protocol = RemoteProtocol.V2 });

        Assert.Contains("\"V2\"", json);
        Assert.Equal(RemoteProtocol.V2, new RemoteSettingsCodec().Deserialize(json)!.Protocol);
    }
}

public class HostScopedBasicAuthHandlerTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public string? Authorization;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<string?> SendAsync(string url)
    {
        var capture = new Capture();
        var handler = new HostScopedBasicAuthHandler(new Uri("https://feed.example.org/v3/index.json"), "bob", "pw") { InnerHandler = capture };
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        return capture.Authorization;
    }

    [Fact]
    public async Task CredentialsGoToTheConfiguredOriginOnly()
    {
        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("bob:pw"));

        Assert.Equal(expected, await SendAsync("https://feed.example.org/v3/registration/x/index.json"));
        Assert.Equal(expected, await SendAsync("https://FEED.example.org/other"));
        Assert.Null(await SendAsync("https://blob.example.org/package.nupkg"));
        Assert.Null(await SendAsync("http://feed.example.org/v3/index.json"));
        Assert.Null(await SendAsync("https://feed.example.org:8443/v3/index.json"));
    }

    [Fact]
    public async Task AnAuthorizationHeaderAlreadyThereIsLeftAlone()
    {
        var capture = new Capture();
        var handler = new HostScopedBasicAuthHandler(new Uri("https://feed.example.org/"), "bob", "pw") { InnerHandler = capture };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://feed.example.org/x");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "t");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer t", capture.Authorization);
    }

    [Fact]
    public void TheFactoryAddsTheHandlerOnlyWhenThereAreCredentials()
    {
        var factory = new RemoteHttpClientFactory();

        using var plain = factory.Create(new RemoteRepositorySettings { Url = "https://h/i.json" });
        using var withCredentials = factory.Create(new RemoteRepositorySettings { Url = "https://h/i.json", Username = "u", Password = "p", DownloadTimeoutSeconds = 7 });

        Assert.Equal(TimeSpan.FromSeconds(7), withCredentials.Timeout);
        Assert.NotNull(plain);
    }
}
