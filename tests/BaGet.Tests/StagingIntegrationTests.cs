using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Staging on the real host, with accounts off: the mode that authenticates with a
/// pasted API key stages files too, so the service must resolve there as well.
/// </summary>
public class StagingIntegrationTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public StagingIntegrationTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output);
        _client = _app.CreateClient();
    }

    [Fact]
    public async Task StagesAndForgetsAPackageWithoutAccounts()
    {
        var staging = Staging();

        var token = await staging.StageAsync(Package(), ownerId: null, CancellationToken.None);

        using (var staged = await staging.OpenAsync(token, ownerId: null, CancellationToken.None))
        {
            Assert.NotNull(staged);
        }

        Assert.True(await staging.RemoveAsync(token, ownerId: null, CancellationToken.None));

        Assert.Null(await staging.OpenAsync(token, ownerId: null, CancellationToken.None));
    }

    [Fact]
    public async Task AStagedFileIsNotServedOverHttp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await Staging().StageAsync(Package(), ownerId: null, CancellationToken.None);

        // No route accepts a storage path: every storage read is composed from a
        // database-resolved ID and version, or from a validated symbol key.
        foreach (var url in new[]
                 {
                     $"staging/{token}.nupkg",
                     $"v3/package/staging/{token}/{token}.nupkg",
                 })
        {
            using var response = await _client.GetAsync(url, cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private IPackageStagingService Staging()
        => _app.Services.GetRequiredService<IPackageStagingService>();

    private static Stream Package() => new MemoryStream(Encoding.UTF8.GetBytes("a package"));

    public void Dispose()
    {
        _app.Dispose();
        _client.Dispose();
    }
}