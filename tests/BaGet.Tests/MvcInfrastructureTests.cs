using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// The controller infrastructure that replaces the Razor Pages programming model, driven through the real pipeline with test
/// controllers: antiforgery, handler selection by <c>?handler=</c>, the controller bases' gates (accounts, sign-in, administrator,
/// read-only), the <c>r/{repo}/</c> twin and the repository rules of <c>[RepositoryPage]</c>, and links to repository pages.
/// </summary>
public class MvcInfrastructureTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly BaGetApplication _app;
    private readonly Browser _anonymous;
    private readonly Browser _alice;
    private readonly Browser _root;

    public MvcInfrastructureTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } },
            configureServices: services => services.AddControllers().AddApplicationPart(typeof(MvcInfrastructureTests).Assembly));
        _anonymous = new Browser(_app);
        _alice = new Browser(_app);
        _root = new Browser(_app);
        SetUpAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _anonymous.Dispose();
        _alice.Dispose();
        _root.Dispose();
        _app.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task SetUpAsync()
    {
        await CreateUserAsync(_app, "alice");
        await CreateUserAsync(_app, "root", administrator: true);
        await SignInAsync(_alice, "alice");
        await SignInAsync(_root, "root");
        await _app.CreateRepositoryAsync("second", Token);
        await _app.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync("closed", RepositoryFormat.NuGet, RepositoryKind.Local, new Dictionary<string, string?>(), anonymousRead: false, Token);
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            return await response.Content.ReadAsStringAsync(Token);
        }
    }

    private async Task<string> PostAsync(Browser browser, string action, Dictionary<string, string>? fields = null)
        => await ReadAsync(await browser.PostAsync("account/login", fields ?? new Dictionary<string, string>(), action: action, tokenPage: "account/login"));

    // ---- antiforgery and handler selection ----

    [Fact]
    public async Task AGetPicksTheDefaultHandlerOrTheNamedOneAndAnUnknownHandlerIsTheDefault()
    {
        Assert.Equal("get", await ReadAsync(await _anonymous.GetAsync("test-ui/handlers")));
        Assert.Equal("copy", await ReadAsync(await _anonymous.GetAsync("test-ui/handlers?handler=CopyStatus")));
        Assert.Equal("copy", await ReadAsync(await _anonymous.GetAsync("test-ui/handlers?handler=copystatus")));
        Assert.Equal("get", await ReadAsync(await _anonymous.GetAsync("test-ui/handlers?handler=NoSuchHandler")));
        // A POST handler's name is not a GET handler.
        Assert.Equal("get", await ReadAsync(await _anonymous.GetAsync("test-ui/handlers?handler=Approve")));
    }

    [Fact]
    public async Task APostPicksTheDefaultHandlerOrTheNamedOne()
    {
        Assert.Equal("post", await PostAsync(_anonymous, "test-ui/handlers"));
        Assert.Equal("approve", await PostAsync(_anonymous, "test-ui/handlers?handler=Approve"));
        Assert.Equal("post", await PostAsync(_anonymous, "test-ui/handlers?handler=Unknown"));
    }

    [Fact]
    public async Task HeadIsAnsweredByTheGetHandler()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, "test-ui/handlers");
        using var response = await _anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task APostWithoutAnAntiforgeryTokenIsRefusedBeforeAnythingElseRuns()
    {
        foreach (var url in new[] { "test-ui/handlers", "test-ui/handlers?handler=Approve", "test-ui/admin" })
        {
            using var response = await _root.RawPostAsync(url, new Dictionary<string, string>());
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    // ---- the account and admin bases ----

    [Fact]
    public async Task AnAdministratorControllerIsForAdministratorsOnly()
    {
        using var anonymous = await _anonymous.GetAsync("test-ui/admin");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("account/login", anonymous.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var user = await _alice.GetAsync("test-ui/admin");
        Assert.Equal(HttpStatusCode.NotFound, user.StatusCode);

        Assert.Equal("admin", await ReadAsync(await _root.GetAsync("test-ui/admin")));
    }

    [Fact]
    public async Task AnAccountControllerAnswers404WhileAccountsAreDisabled()
    {
        using var app = new BaGetApplication(
            _output,
            configureServices: services => services.AddControllers().AddApplicationPart(typeof(MvcInfrastructureTests).Assembly));
        using var browser = new Browser(app);

        using var response = await browser.GetAsync("test-ui/admin");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnAccessControllerAsksThePasswordAndRefusesWritesWith503WhileTheGateIsClosed()
    {
        Assert.Equal("access", await ReadAsync(await _root.GetAsync("test-ui/access")));

        // The wrong password changes nothing and the message says so.
        var denied = await PostAsync(_root, "test-ui/access?handler=Change", new() { { "password", "wrong" } });
        Assert.Equal("denied", denied);
        Assert.Equal("done", await PostAsync(_root, "test-ui/access?handler=Change", new() { { "password", Password } }));

        _app.Services.GetRequiredService<MaintenanceGate>().Close("Copying the database");
        try
        {
            using var response = await _root.PostAsync("account/login", new Dictionary<string, string> { { "password", Password } }, action: "test-ui/access?handler=Change", tokenPage: "account/login");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            // Reading still works.
            Assert.Equal("access", await ReadAsync(await _root.GetAsync("test-ui/access")));
        }
        finally
        {
            _app.Services.GetRequiredService<MaintenanceGate>().Open();
        }
    }

    // ---- repository pages ----

    [Fact]
    public async Task ARepositoryPageIsServedAtItsOwnRouteAndUnderEveryRepositoryOfItsFormat()
    {
        Assert.Equal("repo:default", await ReadAsync(await _anonymous.GetAsync("test-ui/repo")));
        Assert.Equal("repo:second", await ReadAsync(await _anonymous.GetAsync("r/second/test-ui/repo")));
    }

    [Fact]
    public async Task ARepositoryPageAnswers404InARepositoryOfAnotherFormatInAVirtualOneAndInAnUnknownOne()
    {
        using var virtualRepository = await _root.PostAsync("admin/repositories", new Dictionary<string, string> { { "name", "virt" } }, action: "admin/repositories?handler=Virtual");

        foreach (var url in new[] { "r/web/test-ui/repo", "r/virt/test-ui/repo", "r/nothing/test-ui/repo" })
        {
            using var response = await _anonymous.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task ARepositoryPageOfAPrivateRepositoryChallengesAVisitorAndHidesItFromAUserWithoutRead()
    {
        using var anonymous = await _anonymous.GetAsync("r/closed/test-ui/repo");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("account/login", anonymous.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var user = await _alice.GetAsync("r/closed/test-ui/repo");
        Assert.Equal(HttpStatusCode.NotFound, user.StatusCode);

        Assert.Equal("repo:closed", await ReadAsync(await _root.GetAsync("r/closed/test-ui/repo")));
    }

    [Fact]
    public async Task LinksToARepositoryPageStayInsideTheRepository_ThroughTheActionNameWithAndWithoutTheController()
    {
        var plain = await ReadAsync(await _anonymous.GetAsync("test-ui/repo/links"));
        var inSecond = await ReadAsync(await _anonymous.GetAsync("r/second/test-ui/repo/links"));

        Assert.Equal("/test-ui/repo|/test-ui/repo", plain);
        Assert.Equal("/r/second/test-ui/repo|/r/second/test-ui/repo", inSecond);
    }

    [Fact]
    public async Task TheControllersOwnCheckRunsBeforeTheRepositoryCheck()
    {
        // The controller answers 418 before the repository check would answer 404 (npm repository, NuGet page).
        using var response = await _anonymous.GetAsync("r/web/test-ui/order");

        Assert.Equal((HttpStatusCode)418, response.StatusCode);
    }

    [Fact]
    public async Task ARedirectToARepositoryActionStaysInTheRepository()
    {
        using var response = await _anonymous.GetAsync("r/second/test-ui/repo/redirect");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/r/second/test-ui/repo", response.Headers.Location!.ToString());
    }
}

// The controllers below live in the test assembly; the tests above register it as an application part.

/// <summary>Handler selection and antiforgery on a controller that needs no sign-in.</summary>
[Route("test-ui/handlers")]
public sealed class TestHandlersController : UiControllerBase
{
    [HttpGet, HttpHead, PageHandler]
    public IActionResult Index() => Content("get");

    [HttpGet, PageHandler("CopyStatus")]
    public IActionResult CopyStatus() => Content("copy");

    [HttpPost, PageHandler]
    public IActionResult Post() => Content("post");

    [HttpPost, PageHandler("Approve")]
    public IActionResult Approve() => Content("approve");
}

/// <summary>An administrator-only controller.</summary>
[Route("test-ui/admin")]
public sealed class TestAdminController : AdminControllerBase
{
    [HttpGet, PageHandler]
    public IActionResult Index() => Content("admin");

    [HttpPost, PageHandler]
    public IActionResult Post() => Content("admin-post");
}

/// <summary>An access-control controller: password confirmation and the read-only gate.</summary>
[Route("test-ui/access")]
public sealed class TestAccessController : AccessControllerBase
{
    [HttpGet, PageHandler]
    public IActionResult Index() => Content("access");

    [HttpPost, PageHandler("Change")]
    public async Task<IActionResult> Change([FromForm] string? password)
    {
        await ActAsync(password, confirm: true, () => Task.FromResult("done"), () => Ok());

        return Content(Message ?? (Error != null ? "denied" : "?"));
    }
}

/// <summary>A repository page: <c>/test-ui/repo</c> and <c>/r/{repo}/test-ui/repo</c> in NuGet repositories that are not virtual.</summary>
[Route("test-ui/repo")]
[RepositoryPage(NotOnVirtual = true)]
public sealed class TestRepositoryController : UiControllerBase
{
    [HttpGet, HttpHead, PageHandler]
    public IActionResult Index([FromServices] IRepositoryContext repository) => Content("repo:" + repository.Current.Name);

    [HttpGet("links")]
    public IActionResult Links()
        => Content(string.Join("|", Url.Action(nameof(Index)), Url.Action(nameof(Index), "TestRepository")));

    [HttpGet("redirect")]
    public IActionResult Redirect() => RedirectToAction(nameof(Index));
}

/// <summary>A repository page whose own check answers first.</summary>
[Route("test-ui/order")]
[RepositoryPage]
public sealed class TestOrderController : UiControllerBase
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context) => context.Result = StatusCode(418);

    [HttpGet]
    public IActionResult Index() => Content("never");
}
