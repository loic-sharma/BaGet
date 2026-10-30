using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Ownership;
using BaGet.Web.Application.UseCases;

namespace BaGet.Tests;

/// <summary>
/// Shared by the browser upload tests: driving the two steps of the upload page the way a
/// browser drives them.
/// </summary>
internal static class BrowserUpload
{
    /// <summary>
    /// A page that always renders a form, and so always yields an antiforgery token. The
    /// upload page renders no form in read-only mode, and the token is per session, not
    /// per form.
    /// </summary>
    public const string TokenPage = "packages/Any.Package/1.0.0/report-abuse";

    public static Task<HttpResponseMessage> StageAsync(
        this Browser browser,
        string fileName,
        Stream? file,
        string? apiKey = null,
        string? tokenPage = null)
    {
        var fields = new Dictionary<string, string>();
        if (apiKey != null) fields["apiKey"] = apiKey;

        return browser.PostFileAsync(
            "upload?handler=Stage",
            "package",
            fileName,
            file,
            fields,
            tokenPage);
    }

    public static Task<HttpResponseMessage> SubmitAsync(
        this Browser browser,
        string token,
        bool acceptTerms = true,
        string? apiKey = null,
        string? tokenPage = null)
    {
        var fields = new Dictionary<string, string> { { "token", token } };
        if (acceptTerms) fields["acceptTerms"] = "true";
        if (apiKey != null) fields["apiKey"] = apiKey;

        return browser.PostAsync("upload", fields, "upload?handler=Submit", tokenPage);
    }

    public static Task<HttpResponseMessage> CancelAsync(
        this Browser browser,
        string token,
        string? apiKey = null,
        string? tokenPage = null)
    {
        var fields = new Dictionary<string, string> { { "token", token } };
        if (apiKey != null) fields["apiKey"] = apiKey;

        return browser.PostAsync("upload", fields, "upload?handler=Cancel", tokenPage);
    }

    /// <summary>
    /// The staging token the preview is carrying, which is the only handle on a staged
    /// upload.
    /// </summary>
    public static string StagedToken(string html)
    {
        var match = Regex.Match(html, "name=\"token\" value=\"([0-9a-f]{32})\"");

        Assert.True(match.Success, "The preview did not render a staging token.");

        return match.Groups[1].Value;
    }
}

/// <summary>
/// The browser upload with accounts on: the signed-in session is the identity, and
/// ownership decides what it may publish.
/// </summary>
public class BrowserUploadTests : IDisposable
{
    private readonly BaGetApplication _app;

    public BrowserUploadTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", "global-key" },
            { "Accounts:Enabled", "true" },
        });
    }

    [Fact]
    public async Task PublishesAPackageThroughTheTwoSteps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = await SignInAsync("alice");

        var preview = await StageAsync(alice, "TestData.1.2.3.nupkg", TestResources.GetResourceStream(TestResources.Package));

        // Step one shows what the file contains, and publishes nothing.
        Assert.Contains("TestData 1.2.3", preview);
        Assert.Contains("Test description", preview);
        Assert.Contains("Test author", preview);
        Assert.False(await ExistsAsync("TestData", "1.2.3"));

        var token = BrowserUpload.StagedToken(preview);

        using var published = await alice.SubmitAsync(token);
        var html = await published.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Contains("TestData 1.2.3 was published", html);
        Assert.True(await ExistsAsync("TestData", "1.2.3"));

        // The package is served, so the upload went through the same pipeline a push does.
        using var download = await alice.GetAsync("v3/package/testdata/1.2.3/testdata.1.2.3.nupkg");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);

        // And the staged copy is gone: the token no longer names anything.
        using var again = await alice.SubmitAsync(token);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Contains(UploadUseCase.UnknownTokenMessage, await again.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task ABrandNewIdMakesTheUploaderItsOwner()
    {
        using var alice = await SignInAsync("alice");

        await PublishAsync(alice, "Alice.New", "1.0.0");

        Assert.Equal(new[] { "alice" }, await OwnersAsync("Alice.New"));
    }

    [Fact]
    public async Task AnIdOwnedByAnotherUserIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = await SignInAsync("alice");
        using var bob = await SignInAsync("bob");

        await PublishAsync(alice, "Alice.Package", "1.0.0");

        using var response = await bob.StageAsync("Alice.Package.2.0.0.nupkg", TestPackages.Build("Alice.Package", "2.0.0"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("You may not publish", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.False(await ExistsAsync("Alice.Package", "2.0.0"));
    }

    [Fact]
    public async Task AnIdClaimedWhileThePreviewWasOpenIsRefusedAtSubmission()
    {
        using var alice = await SignInAsync("alice");
        using var bob = await SignInAsync("bob");

        // Nobody owns the ID when Alice stages, so staging is allowed.
        var token = BrowserUpload.StagedToken(
            await StageAsync(alice, "Race.1.0.0.nupkg", TestPackages.Build("Race", "1.0.0")));

        // Bob claims it while the preview is on screen.
        await PublishAsync(bob, "Race", "0.9.0");

        using var response = await alice.SubmitAsync(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await ExistsAsync("Race", "1.0.0"));
        Assert.Equal(new[] { "bob" }, await OwnersAsync("Race"));
    }

    [Fact]
    public async Task AnotherUsersTokenIsUnknownAndLeavesTheirUploadAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = await SignInAsync("alice");
        using var bob = await SignInAsync("bob");

        var token = BrowserUpload.StagedToken(
            await StageAsync(alice, "Alice.1.0.0.nupkg", TestPackages.Build("Alice.Shared", "1.0.0")));

        using var stolen = await bob.SubmitAsync(token);

        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
        Assert.Contains(UploadUseCase.UnknownTokenMessage, await stolen.Content.ReadAsStringAsync(cancellationToken));
        Assert.False(await ExistsAsync("Alice.Shared", "1.0.0"));

        // Alice's own upload is untouched, so Bob's attempt did not consume it.
        using var mine = await alice.SubmitAsync(token);

        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.True(await ExistsAsync("Alice.Shared", "1.0.0"));
    }

    [Fact]
    public async Task CancellingMakesTheTokenUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = await SignInAsync("alice");

        var token = BrowserUpload.StagedToken(
            await StageAsync(alice, "Cancelled.1.0.0.nupkg", TestPackages.Build("Cancelled", "1.0.0")));

        using var cancelled = await alice.CancelAsync(token);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Contains("The upload was discarded", await cancelled.Content.ReadAsStringAsync(cancellationToken));

        using var reused = await alice.SubmitAsync(token);

        Assert.Equal(HttpStatusCode.NotFound, reused.StatusCode);
        Assert.Contains(UploadUseCase.UnknownTokenMessage, await reused.Content.ReadAsStringAsync(cancellationToken));
        Assert.False(await ExistsAsync("Cancelled", "1.0.0"));
    }

    [Fact]
    public async Task TheSameVersionTwiceIsRefusedWithAMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = await SignInAsync("alice");

        await PublishAsync(alice, "Twice", "1.0.0");

        var token = BrowserUpload.StagedToken(
            await StageAsync(alice, "Twice.1.0.0.nupkg", TestPackages.Build("Twice", "1.0.0")));

        using var response = await alice.SubmitAsync(token);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Twice 1.0.0 is already on this server", html);

        // A readable sentence, not a stack trace.
        Assert.DoesNotContain("BaGet.Core.PackageIndexingService", html);
        Assert.DoesNotContain("   at ", html);
    }

    [Fact]
    public async Task TheTermsMustBeAcceptedAndTheUploadSurvivesRefusing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = await SignInAsync("alice");

        var token = BrowserUpload.StagedToken(
            await StageAsync(alice, "Terms.1.0.0.nupkg", TestPackages.Build("Terms", "1.0.0")));

        using var refused = await alice.SubmitAsync(token, acceptTerms: false);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(UploadUseCase.TermsMessage, await refused.Content.ReadAsStringAsync(cancellationToken));
        Assert.False(await ExistsAsync("Terms", "1.0.0"));

        // The preview is still there, so ticking the box is all that is needed.
        using var accepted = await alice.SubmitAsync(token);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True(await ExistsAsync("Terms", "1.0.0"));
    }

    [Fact]
    public async Task SymbolsArePublishedForAVersionThatExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = await SignInAsync("alice");

        var packageToken = BrowserUpload.StagedToken(
            await StageAsync(alice, "TestData.1.2.3.nupkg", TestResources.GetResourceStream(TestResources.Package)));
        using (var published = await alice.SubmitAsync(packageToken))
        {
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        }

        var preview = await StageAsync(
            alice,
            "TestData.1.2.3.snupkg",
            TestResources.GetResourceStream(TestResources.SymbolPackage));

        // Symbols carry no metadata, so the preview reports the PDBs it found.
        Assert.Contains("TestData.pdb", preview);

        using var response = await alice.SubmitAsync(BrowserUpload.StagedToken(preview));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("symbols for TestData 1.2.3 were published", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task SymbolsForAVersionThatDoesNotExistAreRefusedAndClaimNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = await SignInAsync("alice");

        var preview = await StageAsync(
            alice,
            "TestData.1.2.3.snupkg",
            TestResources.GetResourceStream(TestResources.SymbolPackage));

        // Said at step one, rather than letting the upload be confirmed and then fail.
        Assert.Contains("Push TestData 1.2.3 first", preview);

        using var response = await alice.SubmitAsync(BrowserUpload.StagedToken(preview));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(
            "Symbols can only be uploaded for a package version already on this server",
            await response.Content.ReadAsStringAsync(cancellationToken));

        // A symbol upload is never a first upload: it creates no package and no owner.
        Assert.False(await ExistsAsync("TestData", "1.2.3"));
        #pragma warning disable CS8604
        Assert.Empty(await OwnersAsync("TestData"));
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task AnotherUsersTokenCannotDiscardTheirUpload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = await SignInAsync("alice");
        using var bob = await SignInAsync("bob");

        var token = BrowserUpload.StagedToken(
            await StageAsync(alice, "Alice.1.0.0.nupkg", TestPackages.Build("Alice.Discard", "1.0.0")));

        using (var stolen = await bob.CancelAsync(token))
        {
            // Indistinguishable from a token that never existed, as submitting is.
            Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
            Assert.Contains(UploadUseCase.UnknownTokenMessage, await stolen.Content.ReadAsStringAsync(cancellationToken));
        }

        // Alice's upload is untouched, so Bob could not make her upload the file again.
        using var mine = await alice.SubmitAsync(token);

        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.True(await ExistsAsync("Alice.Discard", "1.0.0"));
    }

    [Fact]
    public async Task AnAnonymousVisitorCannotDiscardAStagedUpload()
    {
        using var alice = await SignInAsync("alice");
        using var anonymous = new Browser(_app);

        var token = BrowserUpload.StagedToken(
            await StageAsync(alice, "Alice.1.0.0.nupkg", TestPackages.Build("Alice.Anonymous", "1.0.0")));

        using (var attempt = await anonymous.PostAsync(
                   "upload",
                   new Dictionary<string, string> { { "token", token } },
                   "upload?handler=Cancel",
                   BrowserUpload.TokenPage))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        using var mine = await alice.SubmitAsync(token);

        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.True(await ExistsAsync("Alice.Anonymous", "1.0.0"));
    }

    [Fact]
    public async Task AFileNamedSnupkgThatIsNotASymbolPackageIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = await SignInAsync("alice");

        // The content decides the pipeline, so a symbols file name over package content
        // must not be published as a package under that ID - the symbol endpoint would
        // read the very same bytes as symbols.
        using var response = await alice.StageAsync(
            "NotSymbols.1.0.0.snupkg",
            TestPackages.Build("NotSymbols", "1.0.0"));
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SymbolsPackage", html);
        Assert.False(await ExistsAsync("NotSymbols", "1.0.0"));
    }

    [Fact]
    public async Task AnAnonymousVisitorIsAskedToSignIn()
    {
        using var anonymous = new Browser(_app);

        var html = await anonymous.GetPageAsync("upload");

        // Anonymous visitors are sent to sign in rather than shown a form that could only
        // fail, and the push instructions stay.
        Assert.Contains("account/login", html);
        Assert.DoesNotContain("name=\"package\"", html);
        Assert.Contains("dotnet nuget push", html);
    }

    private async Task<Browser> SignInAsync(string userName)
    {
        var browser = new Browser(_app);
        await browser.RegisterAsync(userName);

        return browser;
    }

    private static async Task<string> StageAsync(Browser browser, string fileName, Stream? file)
    {
        using var response = await browser.StageAsync(fileName, file);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return html;
    }

    private static async Task PublishAsync(Browser browser, string id, string version)
    {
        var token = BrowserUpload.StagedToken(
            await StageAsync(browser, $"{id}.{version}.nupkg", TestPackages.Build(id, version)));

        using var response = await browser.SubmitAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<bool> ExistsAsync(string? id, string version)
    {
        using var scope = _app.Services.CreateScope();
        var packages = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();

        return await packages.ExistsAsync(id, NuGet.Versioning.NuGetVersion.Parse(version), CancellationToken.None);
    }

    private async Task<IReadOnlyList<string>?> OwnersAsync(string? id)
    {
        using var scope = _app.Services.CreateScope();
        var ownership = scope.ServiceProvider.GetRequiredService<PackageOwnershipService>();

        #pragma warning disable CS8619
        return (await ownership.GetOwnersAsync(id, CancellationToken.None))
            .Select(o => o.OwnerName)
            .ToList();
        #pragma warning restore CS8619
    }

    public void Dispose() => _app.Dispose();
}

/// <summary>
/// The browser upload with accounts off: there is no user to own an upload, so the pusher
/// proves itself with the same API key the push endpoint requires. The page must not 404
/// here - it is not an account page.
/// </summary>
public class BrowserUploadWithoutAccountsTests : IDisposable
{
    private const string GlobalKey = "global-key";

    private readonly BaGetApplication _app;

    public BrowserUploadWithoutAccountsTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", GlobalKey },
        });
    }

    [Fact]
    public async Task ThePageOffersTheFormAndNeverAnswersNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = new Browser(_app);

        using var response = await browser.GetAsync("upload");
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"package\"", html);
        Assert.Contains("name=\"apiKey\"", html);
    }

    [Fact]
    public async Task TheRightApiKeyPublishes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = new Browser(_app);

        using var staged = await browser.StageAsync(
            "TestData.1.2.3.nupkg",
            TestResources.GetResourceStream(TestResources.Package),
            GlobalKey);
        var preview = await staged.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);

        using var published = await browser.SubmitAsync(BrowserUpload.StagedToken(preview), apiKey: GlobalKey);

        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Contains("TestData 1.2.3 was published", await published.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task TheWrongApiKeyIsRefusedWithoutStagingAnything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = new Browser(_app);

        using var response = await browser.StageAsync(
            "TestData.1.2.3.nupkg",
            TestResources.GetResourceStream(TestResources.Package),
            "not-the-key");
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        // 401, and not the 404 an account page would answer with accounts disabled.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(UploadUseCase.ApiKeyRejectedMessage, html);
        Assert.DoesNotMatch("name=\"token\" value=\"[0-9a-f]{32}\"", html);
    }

    [Fact]
    public async Task TheApiKeyIsNeverWrittenIntoThePage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = new Browser(_app);

        using var staged = await browser.StageAsync(
            "TestData.1.2.3.nupkg",
            TestResources.GetResourceStream(TestResources.Package),
            GlobalKey);
        var html = await staged.Content.ReadAsStringAsync(cancellationToken);

        // Asserted first: a refusal's body carries no API key either, so without this the
        // test would stay green against an upload page that never previewed anything.
        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
        BrowserUpload.StagedToken(html);

        // The preview asks for the key again rather than carrying it in a hidden field.
        Assert.DoesNotContain(GlobalKey, html);
    }

    public void Dispose() => _app.Dispose();
}

/// <summary>
/// Read-only mode refuses both modes of the page, as the push endpoint does.
/// </summary>
public class BrowserUploadReadOnlyTests
{
    private readonly ITestOutputHelper _output;

    public BrowserUploadReadOnlyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TheApiKeyModeIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var app = Application(accounts: false);
        using var browser = new Browser(app);

        using var response = await browser.StageAsync(
            "TestData.1.2.3.nupkg",
            TestResources.GetResourceStream(TestResources.Package),
            "global-key",
            BrowserUpload.TokenPage);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(UploadUseCase.ReadOnlyMessage, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task ASignedInSessionIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var app = Application(accounts: true);
        using var browser = new Browser(app);
        await browser.RegisterAsync("alice");

        using var response = await browser.StageAsync(
            "TestData.1.2.3.nupkg",
            TestResources.GetResourceStream(TestResources.Package),
            tokenPage: BrowserUpload.TokenPage);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(UploadUseCase.ReadOnlyMessage, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task ThePushInstructionsAreNotOfferedWhilePushingIsRefused()
    {
        using var app = Application(accounts: false);
        using var browser = new Browser(app);

        var html = await browser.GetPageAsync("upload");

        // The page must not refuse an upload and then hand out commands that answer 401.
        Assert.Contains("upload-read-only", html);
        Assert.DoesNotContain("You can push packages using the service index", html);
        Assert.DoesNotContain("nuget push -Source", html);
    }

    private BaGetApplication Application(bool accounts)
        => new(_output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", "global-key" },
            { "IsReadOnlyMode", "true" },
            { "Accounts:Enabled", accounts ? "true" : "false" },
        });
}