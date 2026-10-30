using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The shipped default: no API key and no accounts. Nothing can tell one caller from
/// another there, so the browser upload is closed rather than a publish button for
/// whoever finds the page.
/// </summary>
public class BrowserUploadWithoutAnyApiKeyTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public BrowserUploadWithoutAnyApiKeyTests(ITestOutputHelper output)
    {
        // Exactly src/BaGet/appsettings.json as it ships: ApiKey "" and no accounts.
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", "" },
        });
        _client = _app.CreateClient();
    }

    [Fact]
    public async Task TheFormIsNotOfferedWhenNothingCanAuthenticateAnUpload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = new Browser(_app);

        using var response = await browser.GetAsync("upload");
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // No file picker, and no API key box that would be ignored if filled in.
        Assert.DoesNotContain("name=\"package\"", html);
        Assert.DoesNotContain("name=\"apiKey\"", html);
        Assert.Contains("upload-no-api-key", html);
    }

    [Fact]
    public async Task AnAnonymousVisitorCannotStageAnything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = new Browser(_app);

        using var response = await browser.StageAsync(
            "TestData.1.2.3.nupkg",
            TestResources.GetResourceStream(TestResources.Package),
            apiKey: null,
            tokenPage: BrowserUpload.TokenPage);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotMatch("name=\"token\" value=\"[0-9a-f]{32}\"", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.False(await ExistsAsync("TestData", "1.2.3"));
    }

    [Fact]
    public async Task AnEmptyApiKeyBoxIsNotAWayIn()
    {
        using var browser = new Browser(_app);

        using var response = await browser.StageAsync(
            "TestData.1.2.3.nupkg",
            TestResources.GetResourceStream(TestResources.Package),
            apiKey: "",
            tokenPage: BrowserUpload.TokenPage);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(await ExistsAsync("TestData", "1.2.3"));
    }

    [Fact]
    public async Task ThePushEndpointIsUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Closing the browser form must not close the API an open server already has:
        // the page is a convenience, not the authorization model.
        using var package = TestPackages.Build("Open.Package", "1.0.0");
        using var request = TestPackages.PushRequest(package, apiKey: null);
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<bool> ExistsAsync(string? id, string version)
    {
        using var scope = _app.Services.CreateScope();
        var packages = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();

        return await packages.ExistsAsync(id, NuGetVersion.Parse(version), CancellationToken.None);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}

/// <summary>
/// A store that fails while an upload is being staged is reported as a sentence, as the
/// equivalent failure on the confirm step already is - not as a bare 500 or a stack
/// trace.
/// </summary>
public class BrowserUploadStorageFailureTests : IDisposable
{
    private const string GlobalKey = "global-key";

    private readonly BaGetApplication _app;

    public BrowserUploadStorageFailureTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "ApiKey", GlobalKey } },
            configureServices: services => services.Replace(
                ServiceDescriptor.Scoped<IPackageStagingService>(_ => new UnwritableStaging())));
    }

    [Fact]
    public async Task AStoreThatCannotHoldTheUploadIsReportedAsASentence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = new Browser(_app);

        using var response = await browser.StageAsync(
            "TestData.1.2.3.nupkg",
            TestResources.GetResourceStream(TestResources.Package),
            GlobalKey);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("could not store", html);
        Assert.DoesNotContain("   at ", html);
        Assert.DoesNotContain("the disk is full", html);
    }

    public void Dispose() => _app.Dispose();

    private sealed class UnwritableStaging : IPackageStagingService
    {
        public Task<string> StageAsync(Stream? package, string? ownerId, CancellationToken cancellationToken = default)
            => throw new IOException("the disk is full");

        #pragma warning disable CS8619, CS8625
        public Task<Stream?> OpenAsync(string token, string? ownerId, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(null);
        #pragma warning restore CS8619, CS8625

        public Task<bool> RemoveAsync(string? token, string? ownerId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<int> SweepAsync(DateTime stagedBefore, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}

/// <summary>
/// Discarding a staged upload is a write to storage and a loss to its owner, so it is
/// refused on a read-only server exactly as publishing is.
/// </summary>
public class BrowserUploadCancelReadOnlyTests : IDisposable
{
    private readonly BaGetApplication _app;

    public BrowserUploadCancelReadOnlyTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", "global-key" },
            { "Accounts:Enabled", "true" },
            { "IsReadOnlyMode", "true" },
        });
    }

    [Fact]
    public async Task AReadOnlyServerRefusesToDiscardAStagedUpload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = new Browser(_app);
        await alice.RegisterAsync("alice");

        // Staged through the service: a read-only server refuses the upload itself, and
        // the case being pinned is a server switched to read-only while a preview is open.
        #pragma warning disable CS8602
        var aliceId = await WithScopeAsync(async s =>
            (await s.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync("alice")).Id);
        #pragma warning restore CS8602

        var staging = _app.Services.GetRequiredService<IPackageStagingService>();
        var token = await staging.StageAsync(Package(), aliceId, CancellationToken.None);

        using var response = await alice.CancelAsync(token, tokenPage: BrowserUpload.TokenPage);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("read-only", await response.Content.ReadAsStringAsync(cancellationToken));

        // The upload is still there, so nothing in storage was written.
        using var kept = await staging.OpenAsync(token, aliceId, CancellationToken.None);
        Assert.NotNull(kept);
    }

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _app.Services.CreateScope();

        return await action(scope.ServiceProvider);
    }

    private static Stream? Package() => TestResources.GetResourceStream(TestResources.Package);

    public void Dispose() => _app.Dispose();
}

/// <summary>
/// Abandoned staged uploads are only ever reclaimed by the sweep, and the sweep can only
/// reclaim what the store can enumerate. A store that cannot be swept grows without
/// bound, so every store BaGet ships must be listable.
/// </summary>
public class StorageCanBeSweptTests
{
    [Theory]
    [InlineData(typeof(FileStorageService))]
    [InlineData(typeof(NullStorageService))]
    [InlineData(typeof(BaGet.Azure.BlobStorageService))]
    [InlineData(typeof(BaGet.Aws.S3StorageService))]
    [InlineData(typeof(BaGet.Gcp.GoogleCloudStorageService))]
    [InlineData(typeof(BaGet.Aliyun.AliyunStorageService))]
    public void EveryStoreCanListWhatItHolds(Type storage)
    {
        Assert.True(
            typeof(IListableStorageService).IsAssignableFrom(storage),
            $"{storage.Name} cannot be swept: it does not implement {nameof(IListableStorageService)}, "
            + "so abandoned staged uploads would accumulate in it forever.");
    }
}
