using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BaGet.Core;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using BaGet.Web.V3.Api.Catalog;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// A screen is its controller and default GET action (<see cref="ScreenRef"/>). Every name that is typed as text (the catalog, the links a format
/// module contributes) must resolve to an action that exists; the names written as text in <c>[UiApi]</c> and
/// <c>[ApiOperation]</c> are checked here too (the API assemblies cannot reference the view controllers).
/// </summary>
public class ScreenCatalogTests : IDisposable
{
    private readonly BaGetApplication _app;

    public ScreenCatalogTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "Validation:Enabled", "true" },
            { "Feeds:EnableV2", "true" },
        });
    }

    public void Dispose() => _app.Dispose();

    private HashSet<ScreenRef> Screens()
        => _app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Select(a => ScreenEndpoints.ScreenOf(a))
            .Where(s => s != null)
            .Select(s => s!)
            .ToHashSet();

    [Fact]
    public void EveryCatalogEntryIsAScreenOfAViewController()
    {
        var screens = Screens();

        Assert.Empty(ScreenCatalog.All.Where(s => !screens.Contains(s)).Select(s => s.ToString()));
    }

    [Fact]
    public void EveryScreenOfAViewControllerIsInTheCatalog()
    {
        var catalog = ScreenCatalog.All.ToHashSet();

        Assert.Empty(Screens().Where(s => !catalog.Contains(s)).Select(s => s.ToString()));
    }

    private IEnumerable<ControllerActionDescriptor> Actions()
        => _app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>();

    [Fact]
    public void EveryUiApiTwinNamesAScreenOfAViewController()
    {
        // The twins name their screen by text (the API assemblies cannot reference the view controllers): resolve each against the registered screens.
        var screens = Screens();
        var twins = Actions().Select(a => a.MethodInfo.GetCustomAttribute<UiApiAttribute>()).Where(a => a != null).ToList();

        Assert.NotEmpty(twins);
        Assert.Empty(twins.Where(a => !screens.Contains(a!.Screen)).Select(a => a!.Screen.ToString()).Distinct());
    }

    [Fact]
    public void EveryApiOperationNamesAScreenOfAViewController()
    {
        var screens = Screens();
        var operations = Actions().Select(a => a.MethodInfo.GetCustomAttribute<ApiOperationAttribute>()).Where(a => a != null).ToList();

        Assert.NotEmpty(operations);
        Assert.Empty(operations.Where(a => !screens.Contains(a!.UiScreen)).Select(a => a!.UiScreen.ToString()).Distinct());
    }

    [Fact]
    public void TheCatalogListsEveryMember()
    {
        var members = typeof(ScreenCatalog).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (ScreenRef)f.GetValue(null)!).ToHashSet();

        Assert.Equal(members.Count, ScreenCatalog.All.Count);
        Assert.True(members.SetEquals(ScreenCatalog.All));
    }

    [Fact]
    public void EveryLinkAFormatContributesOpensAScreen()
    {
        var screens = Screens();
        var registry = _app.Services.GetRequiredService<FormatRegistry>();
        var checkedLinks = 0;

        foreach (var module in registry.Modules)
        {
            foreach (var kind in new[] { RepositoryKind.Local, RepositoryKind.Remote, RepositoryKind.Virtual })
            {
                foreach (var link in module.RepositoryLinks(new RepositoryInfo(1, "x", module.Format, kind)).Where(l => l.Controller != null || l.Action != null))
                {
                    checkedLinks++;
                    Assert.True(screens.Contains(new ScreenRef(link.Controller!, link.Action!)), $"{module.Format} link {link.Id}: {link.Controller}/{link.Action} is no screen.");
                }
            }
        }

        Assert.True(checkedLinks > 0);
    }
}
