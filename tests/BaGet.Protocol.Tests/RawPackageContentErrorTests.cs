using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Tests;

public class RawPackageContentErrorTests
{
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task AnErrorAnswerIsNotThePackage(HttpStatusCode status)
    {
        var client = new RawPackageContentClient(new HttpClient(new Handler(status)), "https://upstream.test/content");
        var version = NuGetVersion.Parse("1.0.0");
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadPackageOrNullAsync("a", version, ct));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadPackageManifestOrNullAsync("a", version, ct));
    }

    [Fact]
    public async Task ANotFoundAnswerIsNullAndItsResponseIsDisposed()
    {
        var handler = new Handler(HttpStatusCode.NotFound);
        var client = new RawPackageContentClient(new HttpClient(handler), "https://upstream.test/content");

        Assert.Null(await client.DownloadPackageOrNullAsync("a", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));
        Assert.True(handler.Content!.Disposed);
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public Handler(HttpStatusCode status) => _status = status;

        public TrackedContent? Content { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Content = new TrackedContent("<html>error</html>");
            return Task.FromResult(new HttpResponseMessage(_status) { Content = Content });
        }
    }

    private sealed class TrackedContent : StringContent
    {
        public TrackedContent(string text) : base(text) { }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
