using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BaGet.Frontend.Mvc.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;
using Xunit;
using BaGet.Frontend.Routing;
using BaGet.Web.V3.Routing;
using BaGet.Frontend.TagHelpers;

namespace BaGet.Web.Tests;

/// <summary>
/// The building blocks controllers use to replace Razor Pages without changing a URL: the <c>r/{repo}/</c> twin of an action's
/// routes, handler selection by <c>?handler=</c>, links to repository actions and navigation.
/// </summary>
public class MvcInfrastructureFacts
{
    // ---- RepositoryActionRouteConvention ----

    [Route("docker-images")]
    private sealed class MarkedController : Controller
    {
        [HttpGet, RepositoryPage]
        public IActionResult Index() => Ok();

        [HttpGet("{**image}"), RepositoryPage]
        public IActionResult Image(string image) => Ok();

        [HttpPost("{**image}"), RepositoryPage]
        public IActionResult Delete(string image) => Ok();

        [HttpGet("elsewhere")]
        public IActionResult NotMarked() => Ok();
    }

    [Route("packages/{id}/manage"), RepositoryPage]
    private sealed class MarkedByControllerController : Controller
    {
        [HttpGet]
        public IActionResult Show(string id) => Ok();
    }

    [Route("/admin/users"), RepositoryPage]
    private sealed class AbsoluteController : Controller
    {
        [HttpGet("~/stats")]
        public IActionResult Stats() => Ok();
    }

    private static ApplicationModel ModelOf(params Type[] controllers)
    {
        var application = new ApplicationModel();
        foreach (var type in controllers)
        {
            var info = type.GetTypeInfo();
            var controller = new ControllerModel(info, info.GetCustomAttributes(inherit: true).ToList());
            foreach (var route in info.GetCustomAttributes<RouteAttribute>())
            {
                controller.Selectors.Add(new SelectorModel { AttributeRouteModel = new AttributeRouteModel(route) });
            }

            foreach (var method in info.DeclaredMethods.Where(m => m.IsPublic && !m.IsStatic))
            {
                var action = new ActionModel(method, method.GetCustomAttributes(inherit: true).ToList()) { Controller = controller, ActionName = method.Name };
                var http = method.GetCustomAttributes().OfType<HttpMethodAttribute>().Single();
                action.Selectors.Add(new SelectorModel
                {
                    AttributeRouteModel = http.Template == null ? null : new AttributeRouteModel(http),
                    ActionConstraints = { new HttpMethodActionConstraint(http.HttpMethods) },
                });
                controller.Actions.Add(action);
            }

            application.Controllers.Add(controller);
        }

        return application;
    }

    private static List<(string? Template, int? Order)> RoutesOf(ActionModel action)
        => action.Selectors.Select(s => (s.AttributeRouteModel?.Template, s.AttributeRouteModel?.Order)).ToList();

    [Fact]
    public void EveryRouteOfAMarkedActionGetsATwinWithAnAbsoluteTemplateAndALowerOrder()
    {
        var application = ModelOf(typeof(MarkedController));

        new RepositoryActionRouteConvention().Apply(application);

        var actions = application.Controllers.Single().Actions.ToDictionary(a => a.ActionName);
        // No own template: the controller's route.
        Assert.Contains(("/r/{repo}/docker-images", (int?)RepositoryActionRouteConvention.TwinOrder), RoutesOf(actions["Index"]));
        // Own template combined with the controller's, a catch-all kept.
        Assert.Contains(("/r/{repo}/docker-images/{**image}", (int?)RepositoryActionRouteConvention.TwinOrder), RoutesOf(actions["Image"]));
        Assert.Contains(("/r/{repo}/docker-images/{**image}", (int?)RepositoryActionRouteConvention.TwinOrder), RoutesOf(actions["Delete"]));
        // The originals are untouched, and a twin keeps the HTTP method of its original.
        Assert.Contains(("{**image}", (int?)null), RoutesOf(actions["Image"]));
        Assert.All(actions["Delete"].Selectors, s => Assert.Contains("POST", s.ActionConstraints.OfType<HttpMethodActionConstraint>().Single().HttpMethods));
    }

    [Fact]
    public void AnActionWithoutTheAttributeKeepsItsRoutes()
    {
        var application = ModelOf(typeof(MarkedController));

        new RepositoryActionRouteConvention().Apply(application);

        Assert.Single(application.Controllers.Single().Actions.Single(a => a.ActionName == "NotMarked").Selectors);
    }

    [Fact]
    public void AControllerLevelAttributeMarksEveryActionAndAnAbsoluteTemplateIsNotCombined()
    {
        var application = ModelOf(typeof(MarkedByControllerController), typeof(AbsoluteController));

        new RepositoryActionRouteConvention().Apply(application);

        var show = application.Controllers[0].Actions.Single();
        Assert.Contains(("/r/{repo}/packages/{id}/manage", (int?)RepositoryActionRouteConvention.TwinOrder), RoutesOf(show));
        var stats = application.Controllers[1].Actions.Single();
        Assert.Contains(("/r/{repo}/stats", (int?)RepositoryActionRouteConvention.TwinOrder), RoutesOf(stats));
    }

    [Fact]
    public void ApplyingTwiceDoesNotTwinTheTwins()
    {
        var application = ModelOf(typeof(MarkedController));
        var convention = new RepositoryActionRouteConvention();

        convention.Apply(application);
        var once = application.Controllers.Single().Actions.Sum(a => a.Selectors.Count);
        convention.Apply(application);

        Assert.Equal(once, application.Controllers.Single().Actions.Sum(a => a.Selectors.Count));
    }

    // ---- PageHandlerAttribute ----

    private sealed class HandlerController : Controller
    {
        [HttpGet, HttpHead, PageHandler]
        public IActionResult Index() => Ok();

        [HttpGet, PageHandler("CopyStatus")]
        public IActionResult CopyStatus() => Ok();

        [HttpPost, PageHandler]
        public IActionResult Post() => Ok();

        [HttpPost, PageHandler("Approve")]
        public IActionResult Approve() => Ok();
    }

    private static bool Accepts(string actionName, string method, string? query = null, string? routeHandler = null)
    {
        var info = typeof(HandlerController).GetTypeInfo();
        var attribute = info.GetMethod(actionName)!.GetCustomAttribute<PageHandlerAttribute>()!;
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        if (query != null) http.Request.QueryString = new QueryString(query);
        var routeData = new RouteData();
        if (routeHandler != null) routeData.Values["handler"] = routeHandler;

        return attribute.IsValidForRequest(new RouteContext(http) { RouteData = routeData }, new ControllerActionDescriptor { ControllerTypeInfo = info });
    }

    [Fact]
    public void ANamedHandlerRunsOnlyForItsName_ComparedIgnoringCase_FromTheQueryOrTheRoute()
    {
        Assert.True(Accepts("Approve", "POST", "?handler=Approve"));
        Assert.True(Accepts("Approve", "POST", "?handler=approve"));
        Assert.True(Accepts("Approve", "POST", routeHandler: "Approve"));
        Assert.False(Accepts("Approve", "POST"));
        Assert.False(Accepts("Approve", "POST", "?handler=Other"));
    }

    [Fact]
    public void TheDefaultHandlerRunsWithoutAHandlerOrForAnUnknownOne_NotForANamedSibling()
    {
        Assert.True(Accepts("Post", "POST"));
        Assert.True(Accepts("Post", "POST", "?handler=NoSuchHandler"));
        Assert.False(Accepts("Post", "POST", "?handler=Approve"));
        Assert.False(Accepts("Post", "POST", "?handler=APPROVE"));
    }

    [Fact]
    public void ANamedHandlerOfAnotherHttpMethodDoesNotHideTheDefaultHandler()
    {
        // "Approve" is a POST handler: a GET naming it renders the page, as for a page.
        Assert.True(Accepts("Index", "GET", "?handler=Approve"));
        Assert.False(Accepts("Index", "GET", "?handler=CopyStatus"));
        Assert.True(Accepts("CopyStatus", "GET", "?handler=CopyStatus"));
        // HEAD is answered by the GET handlers.
        Assert.False(Accepts("Index", "HEAD", "?handler=CopyStatus"));
        Assert.True(Accepts("Index", "HEAD"));
    }

    // ---- NavLinkTagHelper ----

    private static NavLinkTagHelper NavLinkOn(Type controller, string action)
    {
        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = controller.Name.Replace("Controller", string.Empty),
            ActionName = action,
            ControllerTypeInfo = controller.GetTypeInfo(),
            MethodInfo = controller.GetMethod(action)!,
        };
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(descriptor), "test"));

        return new NavLinkTagHelper(new HttpContextAccessor { HttpContext = http });
    }

    private static bool IsActive(NavLinkTagHelper helper)
    {
        var output = new TagHelperOutput("a", new TagHelperAttributeList(), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.Process(new TagHelperContext(new TagHelperAttributeList(), new Dictionary<object, object>(), "test"), output);

        return output.Attributes.ContainsName("class");
    }

    [Fact]
    public void ANavLinkIsActiveOnEveryActionOfTheScreenItPointsAt()
    {
        // The Upload screen is UploadController.Index; a handler of it (POST ?handler=Stage) is the same screen.
        var onScreen = NavLinkOn(typeof(BaGet.Web.V3.Controllers.FrontEnd.UploadController), "Index");
        onScreen.Controller = "Upload";
        onScreen.Action = "Index";
        Assert.True(IsActive(onScreen));

        var onHandler = NavLinkOn(typeof(BaGet.Web.V3.Controllers.FrontEnd.UploadController), "Stage");
        onHandler.Controller = "Upload";
        onHandler.Action = "Index";
        Assert.True(IsActive(onHandler));

        var other = NavLinkOn(typeof(BaGet.Web.V3.Controllers.FrontEnd.UploadController), "Index");
        other.Controller = "Statistics";
        other.Action = "Index";
        Assert.False(IsActive(other));
    }

    // ---- RepositoryUrlHelper: actions ----

    private sealed class FakeActions : IActionDescriptorCollectionProvider
    {
        public FakeActions(params ActionDescriptor[] items) => ActionDescriptors = new ActionDescriptorCollection(items, 1);

        public ActionDescriptorCollection ActionDescriptors { get; }
    }

    private static ControllerActionDescriptor Action(string controller, string action, bool repository, string? handler = null, string verb = "GET")
    {
        var metadata = new List<object>();
        if (repository) metadata.Add(new RepositoryPageAttribute());
        var method = handler == null
            ? typeof(HandlerController).GetMethod(verb == "GET" ? "Index" : "Post")!
            : typeof(HandlerController).GetMethod(handler)!;

        return new ControllerActionDescriptor
        {
            ControllerName = controller,
            ActionName = action,
            MethodInfo = method,
            ControllerTypeInfo = typeof(HandlerController).GetTypeInfo(),
            EndpointMetadata = metadata,
            ActionConstraints = new List<IActionConstraintMetadata> { new HttpMethodActionConstraint(new[] { verb }) },
        };
    }

    private sealed class RecordingHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; init; } = new();
        public UrlRouteContext? LastRoute { get; private set; }
        public UrlActionContext? LastAction { get; private set; }
        public string? Action(UrlActionContext actionContext) { LastAction = actionContext; return "ok"; }
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) { LastRoute = routeContext; return "ok"; }
    }

    private static (RepositoryUrlHelper Helper, RecordingHelper Inner) HelperFor(string? repo, params ActionDescriptor[] actions)
    {
        var routeData = new RouteData();
        if (repo != null) routeData.Values["repo"] = repo;
        var inner = new RecordingHelper { ActionContext = new ActionContext { HttpContext = new DefaultHttpContext(), RouteData = routeData } };

        return (new RepositoryUrlHelper(inner, new UiActionMap(new FakeActions(actions))), inner);
    }

    private static RouteValueDictionary Values(object? values) => values as RouteValueDictionary ?? new RouteValueDictionary(values);

    [Fact]
    public void ALinkToARepositoryActionKeepsTheAmbientRepository()
    {
        var (helper, inner) = HelperFor("second", Action("Statistics", "Index", true));

        helper.Action("Index", "Statistics");
        Assert.Equal("second", Values(inner.LastAction!.Values)["repo"]);

        // repo = null leaves the repository.
        helper.Action("Index", "Statistics", new { repo = (string?)null });
        Assert.True(Values(inner.LastAction!.Values).ContainsKey("repo"));
        Assert.Null(Values(inner.LastAction!.Values)["repo"]);
    }

    [Fact]
    public void ALinkToAnActionThatIsNotARepositoryPageIsLeftAlone()
    {
        var (helper, inner) = HelperFor("second", Action("Legal", "Privacy", false));

        helper.Action("Privacy", "Legal");
        Assert.False(Values(inner.LastAction!.Values).ContainsKey("repo"));
    }
}
