using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The standing UI-coverage audit (docs/ui-coverage.md): a controller, Razor page or CLI command added without a row in the
/// capability table fails here, so "every operation has a UI counterpart" cannot rot silently. The administration REST API is
/// audited operation by operation by <c>AdminApiAccessTests</c>.
/// </summary>
public class UiCoverageInventoryTests : IDisposable
{
    private readonly BaGetApplication _app;

    public UiCoverageInventoryTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "Accounts:Enabled", "true" },
                { "Validation:Enabled", "true" },
                { "Feeds:EnableV2", "true" },
            });
    }

    public void Dispose() => _app.Dispose();

    /// <summary>
    /// Each protocol controller and the text of docs/ui-coverage.md that names the capability it serves and its page.
    /// Controllers of <c>/api/v1</c> are audited by <c>AdminApiAccessTests</c>, those of <c>/api/ui</c> (twins of a screen) by <c>UiApiParityTests</c>.
    /// </summary>
    private static readonly Dictionary<string, string> ControllerRows = new()
    {
        ["Catalog"] = "Catalog index, pages, leaves",
        ["Maven"] = "Read file, checksum, `maven-metadata.xml`",
        ["Npm"] = "Publish",
        ["Oci"] = "Pull: blobs (ranges), manifests, tag list, referrers",
        ["PackageContent"] = "Package page, versions, dependents, readme, icon, download",
        ["PackageMetadata"] = "Registration, flat container, `.nuspec`",
        ["PackageMetadataAdmin"] = "Deprecation (reasons, message, alternate ID and version range)",
        ["PackagePublish"] = "Unlist / relist / delete",
        ["RepositorySignatures"] = "Repository signatures, certificate",
        ["Search"] = "Search, autocomplete",
        ["ServiceIndex"] = "Service index `v3/index.json`",
        ["Symbol"] = "Symbol download",
        ["V2Feed"] = "V2 feed",
        ["ValidationStatus"] = "Validation status of a submission",
        ["Vulnerability"] = "Vulnerability index",
    };

    /// <summary>Pages with no capability of their own: sign-in and legal pages, covered by the "Register, confirm email, sign in" row.</summary>
    private static readonly HashSet<string> AccountPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Account/ConfirmEmail", "Account/ForgotPassword", "Account/Login", "Account/LoginWith2fa", "Account/LoginWithRecoveryCode",
        "Account/Logout", "Account/Reauthenticate", "Account/Register", "Account/ResetPassword", "AccountPackages/MyProfile",
        "AdminOperations/AccessUnavailable", "Legal/Privacy", "Legal/Terms",
    };

    private static string Docs => File.ReadAllText(Path.Combine(AdminApiAccessTests.RepositoryRoot(), "docs", "ui-coverage.md"));

    [Fact]
    public void EveryProtocolControllerHasARowInTheCoverageTable()
    {
        var provider = _app.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var all = provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(d => d.AttributeRouteInfo?.Template?.StartsWith("api/v1", StringComparison.Ordinal) != true)
            // The JSON twins of the screens (api/ui) are covered through their screen: UiApiParityTests requires each to serve one.
            .Where(d => d.AttributeRouteInfo?.Template?.StartsWith("api/ui/", StringComparison.Ordinal) != true)
            .ToList();
        // A view controller serves screens: EveryScreenIsNamedInTheCoverageTable audits each by its controller and action.
        // It is recognised by its base class (a fallback action such as NoHandler is no screen, but its controller is).
        var pageControllers = all.Where(d => typeof(UiControllerBase).IsAssignableFrom(d.ControllerTypeInfo.AsType())).Select(d => d.ControllerName).ToHashSet();
        var controllers = all
            .Select(d => d.ControllerName)
            .Where(c => !pageControllers.Contains(c))
            .Distinct()
            .ToList();
        Assert.NotEmpty(controllers);

        var docs = Docs;
        foreach (var controller in controllers)
        {
            Assert.True(ControllerRows.TryGetValue(controller, out var row), $"{controller}Controller has no row mapped in UiCoverageInventoryTests.ControllerRows: add its capability to docs/ui-coverage.md and here.");
            Assert.True(docs.Contains(row!, StringComparison.Ordinal), $"docs/ui-coverage.md has no row \"{row}\" for {controller}Controller.");
        }

        foreach (var mapped in ControllerRows.Keys) Assert.Contains(mapped, controllers);
    }

    [Fact]
    public void EveryScreenIsNamedInTheCoverageTable()
    {
        // A screen is a Razor Page of a host or an action of a view controller, named `Controller/Action` (controller without the suffix and the
        // action that shows the screen): docs/ui-coverage.md writes that token in the row of the capability the screen serves.
        var endpoints = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var pages = endpoints
            .Select(e => (Name: e.Metadata.GetMetadata<CompiledPageActionDescriptor>()?.ViewEnginePath ?? ScreenEndpoints.ScreenOf(e)?.ToString(), Template: e.RoutePattern.RawText ?? string.Empty))
            .Where(p => p.Name != null)
            .GroupBy(p => p.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Template).ToList(), StringComparer.OrdinalIgnoreCase);
        Assert.True(pages.Count > 50);

        var docs = Docs;
        bool Named(string page, IEnumerable<string> templates)
        {
            if (AccountPages.Contains(page)) return true;

            return docs.Contains("`" + page + "`", StringComparison.Ordinal);
        }

        var missing = pages.Where(p => !Named(p.Key, p.Value)).Select(p => p.Key).ToList();

        Assert.True(missing.Count == 0, "Pages missing from docs/ui-coverage.md: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryCommandLineCommandIsListedWithItsPage()
    {
        var program = string.Concat(Directory.EnumerateFiles(Path.Combine(AdminApiAccessTests.RepositoryRoot(), "src", "BaGet"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(File.ReadAllText));
        var commands = Regex.Matches(program, @"\.Command\(""([a-z-]+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Contains("rebuild", commands);

        var docs = Docs;
        foreach (var command in commands)
        {
            Assert.True(Regex.IsMatch(docs, $@"CLI `[^`]*\b{Regex.Escape(command)}\b"), $"docs/ui-coverage.md has no CLI row for `{command}`.");
        }
    }
}
