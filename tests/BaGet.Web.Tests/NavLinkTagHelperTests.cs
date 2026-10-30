using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Xunit;
using BaGet.Frontend.TagHelpers;

namespace BaGet.Web.Tests;

public class NavLinkTagHelperTests
{
    private static TagHelperOutput Output(params (string Name, string Value)[] attributes)
    {
        var list = new TagHelperAttributeList();
        foreach (var attribute in attributes)
        {
            list.Add(attribute.Name, attribute.Value);
        }

        return new TagHelperOutput(
            "a",
            list,
            (useCachedResult, encoder) =>
                Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
    }

    private static TagHelperContext Context()
        => new(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            "test");

    // The current endpoint is an action of a real view controller: "Upload" is UploadController.Index, "Index" is HomeController.Index.
    private static IHttpContextAccessor Accessor(
        string screen,
        string requestPath = "/",
        bool withEndpoint = true,
        bool withHttpContext = true)
    {
        if (!withHttpContext)
        {
            return new HttpContextAccessor();
        }

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = requestPath;

        if (withEndpoint)
        {
            var controller = screen == "Upload" ? typeof(BaGet.Web.V3.Controllers.FrontEnd.UploadController) : typeof(BaGet.Web.V3.Controllers.FrontEnd.HomeController);
            var descriptor = new ControllerActionDescriptor
            {
                ControllerName = controller.Name.Replace("Controller", string.Empty),
                ActionName = "Index",
                ControllerTypeInfo = controller.GetTypeInfo(),
                MethodInfo = controller.GetMethod("Index")!,
            };

            httpContext.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(descriptor),
                "test"));
        }

        return new HttpContextAccessor { HttpContext = httpContext };
    }

    private static string? ClassOf(TagHelperOutput output)
        => output.Attributes.ContainsName("class")
            ? output.Attributes["class"].Value?.ToString()
            : null;

    [Fact]
    public void MarksTheLinkForTheCurrentPageActive()
    {
        var target = new NavLinkTagHelper(Accessor("Upload")) { Controller = "Upload", Action = "Index" };
        var output = Output();

        target.Process(Context(), output);

        Assert.Equal("active", ClassOf(output));
    }

    [Fact]
    public void LeavesALinkForAnotherPageAlone()
    {
        var target = new NavLinkTagHelper(Accessor("Index")) { Controller = "Upload", Action = "Index" };
        var output = Output();

        target.Process(Context(), output);

        Assert.Null(ClassOf(output));
    }

    [Fact]
    public void KeepsTheClassesTheElementAlreadyHas()
    {
        // The helper targets any element carrying "nav-link", so it must add its class
        // rather than replace whatever the markup specified.
        var target = new NavLinkTagHelper(Accessor("Upload")) { Controller = "Upload", Action = "Index" };
        var output = Output(("class", "nav-item highlighted"));

        target.Process(Context(), output);

        var classes = ClassOf(output);
        Assert.Contains("nav-item", classes);
        Assert.Contains("highlighted", classes);
        Assert.Contains("active", classes);
    }

    [Fact]
    public void DoesNotAddTheClassTwice()
    {
        var target = new NavLinkTagHelper(Accessor("Upload")) { Controller = "Upload", Action = "Index" };
        var output = Output(("class", "active"));

        target.Process(Context(), output);

        Assert.Equal("active", ClassOf(output));
    }

    [Fact]
    public void IsNotActiveWithoutAnHttpContext()
    {
        var target = new NavLinkTagHelper(Accessor("Upload", withHttpContext: false))
        { Controller = "Upload", Action = "Index" };
        var output = Output();

        target.Process(Context(), output);

        Assert.Null(ClassOf(output));
    }

    [Fact]
    public void IsNotActiveWithoutAnEndpoint()
    {
        var target = new NavLinkTagHelper(Accessor("Upload", withEndpoint: false))
        { Controller = "Upload", Action = "Index" };
        var output = Output();

        target.Process(Context(), output);

        Assert.Null(ClassOf(output));
    }

    [Theory]
    [InlineData("/packages")]
    [InlineData("/")]
    public void ThePackagesLinkIsActiveOnBothOfItsRoutes(string requestPath)
    {
        // _Layout.cshtml links to /packages with no asp-controller, and both / and /packages
        // render the Index page.
        var target = new NavLinkTagHelper(Accessor("Index", requestPath));
        var output = Output();

        target.Process(Context(), output);

        Assert.Equal("active", ClassOf(output));
    }

    [Fact]
    public void AHostLinkToARazorPageIsNotTakenForThePackagesLink()
    {
        // A host layout may keep <a asp-page="/MyPage" nav-link>; it names no BaGet screen, so the home page must not light it.
        var target = new NavLinkTagHelper(Accessor("Index", "/"));
        var context = new TagHelperContext(
            new TagHelperAttributeList { { "asp-page", "/MyPage" }, { "nav-link", string.Empty } },
            new Dictionary<object, object>(),
            "test");
        var output = Output();

        target.Process(context, output);

        Assert.Null(ClassOf(output));
    }

    [Theory]
    [InlineData("/Packages")]
    [InlineData("/PACKAGES")]
    public void ThePackagesLinkIsActiveWhateverTheCaseOfTheRequestPath(string requestPath)
    {
        // Routing is case-insensitive, so these all render the package list.
        var target = new NavLinkTagHelper(Accessor("Index", requestPath));
        var output = Output();

        target.Process(Context(), output);

        Assert.Equal("active", ClassOf(output));
    }

    [Fact]
    public void ThePackagesLinkIsActiveWithATrailingSlash()
    {
        var target = new NavLinkTagHelper(Accessor("Index", "/packages/"));
        var output = Output();

        target.Process(Context(), output);

        Assert.Equal("active", ClassOf(output));
    }

    [Fact]
    public void ThePackagesLinkIsNotActiveOnAnUnrelatedPath()
    {
        var target = new NavLinkTagHelper(Accessor("Index", "/something-else"));
        var output = Output();

        target.Process(Context(), output);

        Assert.Null(ClassOf(output));
    }
}
