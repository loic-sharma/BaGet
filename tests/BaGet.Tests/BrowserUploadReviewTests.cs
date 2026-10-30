using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BaGet.Tests
{
    /// <summary>
    /// Publishing leaves nothing behind in staging, and a rejected API key does not cost the
    /// user their staged upload.
    /// </summary>
    public class BrowserUploadCleanupTests : IDisposable
    {
        private const string GlobalKey = "global-key";

        private readonly BaGetApplication _app;

        public BrowserUploadCleanupTests(ITestOutputHelper output)
        {
            _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
            {
                { "ApiKey", GlobalKey },
            });
        }

        [Fact]
        public async Task PublishingRemovesTheStagedPackageBlobAsWellAsItsMetadata()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using var browser = new Browser(_app);
            var token = await StageAsync(browser);

            using var published = await browser.SubmitAsync(token, apiKey: GlobalKey);

            Assert.Contains("was published", await published.Content.ReadAsStringAsync(cancellationToken));
            Assert.False(await StoredAsync($"staging/{token}.nupkg"), "The staged package blob was left behind.");
            Assert.False(await StoredAsync($"staging/{token}.json"));
        }

        [Fact]
        public async Task AWrongKeyOnConfirmKeepsTheUploadAndOffersTheKeyAgain()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using var browser = new Browser(_app);
            var token = await StageAsync(browser);

            using var wrong = await browser.SubmitAsync(token, apiKey: "nope");
            var html = await wrong.Content.ReadAsStringAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal(token, BrowserUpload.StagedToken(html));

            using var retried = await browser.SubmitAsync(token, apiKey: GlobalKey);
            Assert.Contains("was published", await retried.Content.ReadAsStringAsync(cancellationToken));
        }

        [Fact]
        public async Task DiscardingWithoutTheKeyKeepsTheUploadAndTheKeyDiscardsIt()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using var browser = new Browser(_app);
            var token = await StageAsync(browser);

            using var wrong = await browser.CancelAsync(token);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal(token, BrowserUpload.StagedToken(await wrong.Content.ReadAsStringAsync(cancellationToken)));
            Assert.True(await StoredAsync($"staging/{token}.nupkg"));

            using var right = await browser.CancelAsync(token, GlobalKey);
            Assert.Equal(HttpStatusCode.OK, right.StatusCode);
            Assert.False(await StoredAsync($"staging/{token}.nupkg"));
        }

        private static async Task<string> StageAsync(Browser browser)
        {
            using var staged = await browser.StageAsync(
                "TestData.1.2.3.nupkg",
                TestResources.GetResourceStream(TestResources.Package),
                GlobalKey);

            Assert.Equal(HttpStatusCode.OK, staged.StatusCode);

            return BrowserUpload.StagedToken(await staged.Content.ReadAsStringAsync());
        }

        private async Task<bool> StoredAsync(string path)
        {
            using var scope = _app.Services.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();

            try
            {
                using var stream = await storage.GetAsync(path, CancellationToken.None);
                return stream != null;
            }
            catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
            {
                return false;
            }
        }

        public void Dispose() => _app.Dispose();
    }

    /// <summary>
    /// The package is published before ownership is recorded, so a failure recording it is
    /// reported on the page rather than as a bare 500, and the staged upload is still removed.
    /// </summary>
    public class BrowserUploadOwnershipFailureTests : IDisposable
    {
        private const string GlobalKey = "global-key";

        private readonly BaGetApplication _app;

        public BrowserUploadOwnershipFailureTests(ITestOutputHelper output)
        {
            _app = new BaGetApplication(
                output,
                configOverrides: new Dictionary<string, string> { { "ApiKey", GlobalKey } },
                configureServices: services => services.Replace(
                    ServiceDescriptor.Transient<IPackageAuthorizationService, FailingOwnership>()));
        }

        [Fact]
        public async Task AFailureRecordingOwnershipIsReportedAndTheStagedUploadRemoved()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using var browser = new Browser(_app);
            using var staged = await browser.StageAsync(
                "TestData.1.2.3.nupkg",
                TestResources.GetResourceStream(TestResources.Package),
                GlobalKey);
            var token = BrowserUpload.StagedToken(await staged.Content.ReadAsStringAsync(cancellationToken));

            using var response = await browser.SubmitAsync(token, apiKey: GlobalKey);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("was published", html);
            Assert.Contains("Recording you as the owner", html);

            using var again = await browser.SubmitAsync(token, apiKey: GlobalKey);
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        }

        public void Dispose() => _app.Dispose();

        private sealed class FailingOwnership : IPackageAuthorizationService
        {
            public Task<bool> AuthorizeAsync(BaGetPrincipal? principal, PackageAction action, string? packageId, CancellationToken cancellationToken)
                => Task.FromResult(true);

            public Task OnPushedAsync(BaGetPrincipal principal, string? packageId, bool isNewId, CancellationToken cancellationToken)
                => throw new InvalidOperationException("the database is down");
        }
    }

    /// <summary>
    /// With validation on, a confirmed upload is queued, not indexed: there is no package
    /// page to link to yet.
    /// </summary>
    public class BrowserUploadValidationTests : IDisposable
    {
        private const string GlobalKey = "global-key";

        private readonly BaGetApplication _app;

        public BrowserUploadValidationTests(ITestOutputHelper output)
        {
            _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
            {
                { "ApiKey", GlobalKey },
                { "Validation:Enabled", "true" },
            });
        }

        [Fact]
        public async Task AnAcceptedUploadIsNotLinkedToAPageThatDoesNotExistYet()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using var browser = new Browser(_app);
            using var staged = await browser.StageAsync(
                "TestData.1.2.3.nupkg",
                TestResources.GetResourceStream(TestResources.Package),
                GlobalKey);
            var token = BrowserUpload.StagedToken(await staged.Content.ReadAsStringAsync(cancellationToken));

            using var response = await browser.SubmitAsync(token, apiKey: GlobalKey);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            Assert.Contains("is being validated", html);
            Assert.DoesNotContain("View it", html);
        }

        public void Dispose() => _app.Dispose();
    }
}
