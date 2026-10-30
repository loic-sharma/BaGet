using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Two deploys of one release file at once: the loser is refused (409) and must leave the winner's file alone. The overlap is made
/// deterministic by holding the first deploy inside its storage write until the second has had time to start.
/// </summary>
public class MavenConcurrentDeployTests : IDisposable
{
    private readonly BaGetApplication _app;

    public MavenConcurrentDeployTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "ApiKey", "secret-key" } });
        _app.CreateMavenRepositoryAsync("libs", cancellationToken: TestContext.Current.CancellationToken).GetAwaiter().GetResult();
    }

    public void Dispose() => _app.Dispose();

    private async Task<MavenResult> DeployAsync(byte[] content, Func<IStorageService, IStorageService> wrap)
    {
        using var scope = await _app.CreateRepositoryScopeAsync("libs", TestContext.Current.CancellationToken);
        var service = ActivatorUtilities.CreateInstance<MavenRepositoryService>(scope.ServiceProvider, wrap(scope.ServiceProvider.GetRequiredService<IStorageService>()));

        return await service.PutAsync(BaGetPrincipal.Superuser, MavenNames.ParsePath("org/acme/lib/1.0/lib-1.0.jar")!, new MemoryStream(content), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheLoserOfARaceForOneReleaseFileIsRefusedAndTheWinnersFileStays()
    {
        var winner = Encoding.UTF8.GetBytes("the winner's jar");
        var loser = Encoding.UTF8.GetBytes("a different jar");
        var stored = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var first = DeployAsync(winner, inner => new HoldingStorage(inner, stored, release));
        await stored.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var second = DeployAsync(loser, inner => inner);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        release.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(MavenStatus.Created, results[0].Status);
        Assert.Equal(MavenStatus.Conflict, results[1].Status);

        using var client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync("/r/libs/maven/org/acme/lib/1.0/lib-1.0.jar", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(winner, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    private sealed class HoldingStorage : IStorageService
    {
        private readonly IStorageService _inner;
        private readonly TaskCompletionSource _stored;
        private readonly TaskCompletionSource _release;

        public HoldingStorage(IStorageService inner, TaskCompletionSource stored, TaskCompletionSource release)
        {
            _inner = inner;
            _stored = stored;
            _release = release;
        }

        public Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default) => _inner.GetAsync(path, cancellationToken);

        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default) => _inner.GetDownloadUriAsync(path, cancellationToken);

        public async Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            var result = await _inner.PutAsync(path, content, contentType, cancellationToken);
            _stored.TrySetResult();
            await _release.Task;

            return result;
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => _inner.DeleteAsync(path, cancellationToken);
    }
}
