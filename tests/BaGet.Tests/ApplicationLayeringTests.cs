using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BaGet.Frontend.Controllers.Base;
using BaGet.Identity.Application.Screens;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The layering of the UI API split (decision D3): the Application projects hold use cases, DTOs and business services and reference no
/// MVC or Razor assembly; the UI projects hold controllers, views and MVC infrastructure, and the logic of a screen is not in a class named
/// <c>*Workflow</c> (none is left) and a view controller resolves no use case or Application service itself.
/// </summary>
public class ApplicationLayeringTests
{
    private static readonly Assembly[] ApplicationAssemblies =
    {
        typeof(ScreenGate).Assembly,
        typeof(BaGet.Web.Application.UseCases.LegalUseCase).Assembly,
    };

    private static readonly Assembly[] UiAssemblies =
    {
        typeof(UiControllerBase).Assembly,
        typeof(BaGet.Identity.Controllers.FrontEnd.AdminUsersController).Assembly,
        typeof(BaGet.Web.V3.Controllers.FrontEnd.HomeController).Assembly,
    };

    [Fact]
    public void TheApplicationAssembliesReferenceNoMvcOrRazorAssembly()
    {
        foreach (var assembly in ApplicationAssemblies)
        {
            var forbidden = assembly.GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Where(n => n.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal)
                            || n.StartsWith("Microsoft.AspNetCore.Razor", StringComparison.Ordinal)
                            || n.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal)
                            || n.StartsWith("BaGet.Web.V3", StringComparison.Ordinal)
                            || n == "BaGet.Identity")
                .ToList();

            Assert.True(forbidden.Count == 0, $"{assembly.GetName().Name} references {string.Join(", ", forbidden)}: the Application projects must stay free of MVC, Razor and the UI projects.");
        }
    }

    private static readonly Assembly[] ApiAssemblies =
    {
        typeof(BaGet.Frontend.Mvc.UI.UiApiAttribute).Assembly,
        typeof(BaGet.Identity.Extensions.AccountsServiceCollectionExtensions).Assembly,
        typeof(BaGet.Web.V3.Routing.Routes).Assembly,
    };

    [Fact]
    public void TheApiAssembliesReferenceNoFrontEndAssemblyAndDeclareNoViewController()
    {
        // BaGet.Api.Base, BaGet.Identity.Api and BaGet.Web.V3 serve JSON and protocols only; the screens are in the FrontEnd assemblies.
        var frontEnd = UiAssemblies.Select(a => a.GetName().Name!).ToList();
        foreach (var assembly in ApiAssemblies)
        {
            var references = assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(frontEnd.Contains).ToList();
            Assert.True(references.Count == 0, $"{assembly.GetName().Name} references {string.Join(", ", references)}.");

            var views = assembly.GetTypes().Where(t => t.IsClass && (typeof(Microsoft.AspNetCore.Mvc.Controller).IsAssignableFrom(t) || typeof(BaGet.Frontend.Mvc.UI.IScreenController).IsAssignableFrom(t))).Select(t => t.FullName).ToList();
            Assert.True(views.Count == 0, $"{assembly.GetName().Name} declares view controllers: {string.Join(", ", views)}.");
        }

        var root = AdminApiAccessTests.RepositoryRoot();
        foreach (var project in ApiAssemblies.Select(a => a.GetName().Name!))
        {
            var folder = System.IO.Path.Combine(root, "src", project);
            var views = System.IO.Directory.EnumerateFiles(folder, "*.cshtml", System.IO.SearchOption.AllDirectories)
                .Where(f => !f.Contains(System.IO.Path.DirectorySeparatorChar + "obj" + System.IO.Path.DirectorySeparatorChar))
                .ToList();
            Assert.True(views.Count == 0, $"{project} holds views: {string.Join(", ", views)}.");

            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(folder, project + ".csproj"));
            Assert.DoesNotContain("BaGet.Frontend.Base.csproj", text, StringComparison.Ordinal);
            Assert.DoesNotContain("FrontEnd.csproj", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Sdk=\"Microsoft.NET.Sdk.Razor\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheUiAssembliesHoldEveryViewController()
    {
        var viewControllers = UiAssemblies.SelectMany(a => a.GetTypes()).Where(t => !t.IsAbstract && typeof(UiControllerBase).IsAssignableFrom(t)).ToList();
        Assert.NotEmpty(viewControllers);

        // Only the two FrontEnd assemblies declare screens; Frontend.Base holds the bases and the layout.
        Assert.DoesNotContain(typeof(UiControllerBase).Assembly.GetTypes(), t => !t.IsAbstract && typeof(UiControllerBase).IsAssignableFrom(t));
    }

    [Fact]
    public void TheApplicationProjectsDoNotReferenceTheUiProjectsInTheirProjectFiles()
    {
        var root = AdminApiAccessTests.RepositoryRoot();
        foreach (var project in new[] { "BaGet.Identity.Application", "BaGet.Web.Application" })
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "src", project, project + ".csproj"));

            Assert.DoesNotContain("BaGet.Identity.csproj", text, StringComparison.Ordinal);
            Assert.DoesNotContain("BaGet.Web.V3.csproj", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Sdk=\"Microsoft.NET.Sdk.Razor\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoWorkflowClassIsLeftInTheUiProjects()
    {
        var workflows = UiAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name.EndsWith("Workflow", StringComparison.Ordinal) && t.DeclaringType == null)
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(workflows.Count == 0, "Workflow classes in the UI projects:" + Environment.NewLine + string.Join(Environment.NewLine, workflows));
    }

    [Fact]
    public void TheUseCasesLiveInTheApplicationProjects()
    {
        var useCases = ApplicationAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name.EndsWith("UseCase", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToList();

        Assert.Contains("BaGet.Web.Application.UseCases.LegalUseCase", useCases);
        Assert.Contains("BaGet.Web.Application.UseCases.StatisticsUseCase", useCases);
        Assert.DoesNotContain(UiAssemblies.SelectMany(a => a.GetTypes()), t => t.Name.EndsWith("UseCase", StringComparison.Ordinal));
    }

    [Fact]
    public void AViewControllerHoldsNoUseCaseOrApplicationService()
    {
        // The view controller binds, calls the JSON twin in process and renders; the only use case it names is the browser-only
        // external-provider step, which has no twin.
        var viewControllers = UiAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(UiControllerBase).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(viewControllers);

        var offenders = new List<string>();
        foreach (var type in viewControllers)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var held = type.GetFields(all).Select(f => f.FieldType)
                .Concat(type.GetProperties(all).Select(p => p.PropertyType))
                .Concat(type.GetConstructors(all).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType));

            offenders.AddRange(held
                .Where(h => ApplicationAssemblies.Contains(h.Assembly) && h.Name != "ExternalSignInUseCase" && !h.IsEnum && !h.Name.EndsWith("Form", StringComparison.Ordinal))
                .Where(h => h.Name.EndsWith("UseCase", StringComparison.Ordinal) || h.Name.EndsWith("Service", StringComparison.Ordinal))
                .Select(h => type.Name + " holds " + h.Name));
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }
}
