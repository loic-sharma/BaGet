using System.Linq;
using System.Reflection;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Controllers.FrontEnd;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// <c>?handler=</c> selection groups the actions of one screen by controller and route templates
/// (<see cref="PageHandlerAttribute"/>). These tests pin that grouping, so a route edit cannot silently split a screen
/// (a named handler would then stop knocking out the default action) or merge two screens of one controller.
/// </summary>
public class PageHandlerGroupingTests
{
    private static MethodInfo Action(string name) => typeof(AdminPermissionsController).GetMethod(name)!;

    [Fact]
    public void TheActionsOfOneScreenShareAGroupAndTwoScreensOfOneControllerDoNot()
    {
        // admin/permissions (list) and admin/permissions/{name} (detail) are two screens of one controller.
        Assert.Equal(PageHandlerAttribute.ScreenOf(Action(nameof(AdminPermissionsController.Index))), PageHandlerAttribute.ScreenOf(Action(nameof(AdminPermissionsController.Create))));
        Assert.Equal(PageHandlerAttribute.ScreenOf(Action(nameof(AdminPermissionsController.Detail))), PageHandlerAttribute.ScreenOf(Action(nameof(AdminPermissionsController.Save))));
        Assert.NotEqual(PageHandlerAttribute.ScreenOf(Action(nameof(AdminPermissionsController.Create))), PageHandlerAttribute.ScreenOf(Action(nameof(AdminPermissionsController.Save))));
    }

    [Fact]
    public void ActionsThatShareARouteTemplateAreAlwaysInTheSameGroup()
    {
        var controllers = new[] { typeof(AccountController).Assembly, typeof(BaGet.Web.V3.Controllers.FrontEnd.HomeController).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Namespace?.EndsWith(".Controllers.FrontEnd") == true && !t.IsAbstract);

        foreach (var controller in controllers)
        {
            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<PageHandlerAttribute>().Any())
                .ToList();

            foreach (var a in actions)
            {
                var templates = a.GetCustomAttributes<RouteAttribute>().Select(r => r.Template).ToHashSet();
                foreach (var b in actions.Where(b => b != a && b.GetCustomAttributes<RouteAttribute>().Any(r => templates.Contains(r.Template))))
                {
                    Assert.True(
                        PageHandlerAttribute.ScreenOf(a) == PageHandlerAttribute.ScreenOf(b),
                        $"{controller.Name}.{a.Name} and {b.Name} share a route but not a handler group: give them the same [Route] list.");
                }
            }
        }
    }
}
