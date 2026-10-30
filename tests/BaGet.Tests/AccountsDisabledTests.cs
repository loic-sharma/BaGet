using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Accounts are off by default, and an upgraded server must not grow account pages.
/// </summary>
public class AccountsDisabledTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public AccountsDisabledTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output);
        _client = _app.CreateClient();
    }

    [Theory]
    [InlineData("account/login")]
    [InlineData("account/register")]
    [InlineData("account/logout")]
    [InlineData("account/apikeys")]
    public async Task AccountPagesAreNotFound(string url)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync(url, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheLayoutHasNoSignInLink()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var html = await _client.GetStringAsync("upload", cancellationToken);

        Assert.DoesNotContain("account/login", html);
    }

    public void Dispose()
    {
        _app.Dispose();
        _client.Dispose();
    }
}