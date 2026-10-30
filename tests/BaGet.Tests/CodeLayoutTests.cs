using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The layout rules of the UI and application projects, read from their sources (syntax only):
/// one top-level type per file, named like the file; no nested types; the namespace is the project's
/// <c>RootNamespace</c> plus the folder; every controller sits in the folder of its kind.
/// </summary>
public class CodeLayoutTests
{
    private static readonly string[] Projects =
    {
        "BaGet.Api.Base", "BaGet.Catalog", "BaGet.Frontend.Base", "BaGet.Identity.Api", "BaGet.Identity.Application", "BaGet.Identity.FrontEnd", "BaGet.Validation", "BaGet.Web.Application",
        "BaGet.Web.FrontEnd", "BaGet.Web.V3",
    };

    /// <summary>
    /// Files whose namespace is deliberately not the folder: extension-method classes placed in <c>BaGet</c> so that
    /// <c>using BaGet;</c> (which every host has) finds <c>AddBaGet...</c> / <c>MapBaGet...</c>.
    /// </summary>
    private static readonly Dictionary<string, string> NamespaceExceptions = new()
    {
        ["BaGet.Web.V3/Routing/BaGetEndpointBuilder.cs"] = "BaGet",
        ["BaGet.Web.V3/Extensions/IServiceCollectionExtensions.cs"] = "BaGet",
        ["BaGet.Web.FrontEnd/Extensions/FrontEndServiceCollectionExtensions.cs"] = "BaGet",
    };

    private static readonly Regex RootNamespace = new("<RootNamespace>([^<]+)</RootNamespace>", RegexOptions.Compiled);

    public static IEnumerable<object[]> ProjectNames() => Projects.Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(ProjectNames))]
    public void EveryFileHoldsOneTopLevelTypeNamedLikeTheFile(string project)
    {
        var violations = new List<string>();
        foreach (var file in Files(project))
        {
            var types = TopLevelTypes(file.Root).ToList();
            if (types.Count > 1) violations.Add($"{file.Relative}: several types ({string.Join(", ", types.Select(NameOf))})");
            if (types.Count == 1 && NameOf(types[0]) != Path.GetFileNameWithoutExtension(file.Path).Split('.')[0])
                violations.Add($"{file.Relative}: the type is {NameOf(types[0])}");
        }

        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Theory]
    [MemberData(nameof(ProjectNames))]
    public void NoTypeIsNested(string project)
    {
        var violations = new List<string>();
        foreach (var file in Files(project))
        {
            foreach (var nested in file.Root.DescendantNodes().Where(n => IsType(n) && n.Parent is BaseTypeDeclarationSyntax))
                violations.Add($"{file.Relative}: {NameOf(nested)} is nested in {NameOf(nested.Parent!)}");
        }

        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Theory]
    [MemberData(nameof(ProjectNames))]
    public void TheNamespaceIsTheRootNamespaceAndTheFolder(string project)
    {
        var root = RootNamespaceOf(project);
        var violations = new List<string>();
        foreach (var file in Files(project))
        {
            var declared = file.Root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Select(n => n.Name.ToString()).ToList();
            if (declared.Count == 0 && !TopLevelTypes(file.Root).Any()) continue;

            var folder = Path.GetDirectoryName(file.InProject)!.Replace('\\', '/');
            var expected = NamespaceExceptions.TryGetValue(file.Relative, out var exception)
                ? exception
                : folder.Length == 0 ? root : root + "." + folder.Replace('/', '.');
            if (declared.Count != 1 || declared[0] != expected)
                violations.Add($"{file.Relative}: namespace {string.Join("/", declared)}, expected {expected}");
        }

        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void TheNamespaceExceptionsAreStillNeeded()
    {
        foreach (var path in NamespaceExceptions.Keys)
            Assert.True(File.Exists(Path.Combine(SourceRoot(), path)), path + " no longer exists: remove it from the allow-list");
    }

    [Theory]
    [MemberData(nameof(ProjectNames))]
    public void EveryControllerIsInTheFolderOfItsKind(string project)
    {
        var violations = new List<string>();
        foreach (var file in Files(project))
        {
            foreach (var type in TopLevelTypes(file.Root).OfType<ClassDeclarationSyntax>().Where(c => c.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal) || c.Identifier.Text.EndsWith("ControllerBase", StringComparison.Ordinal)))
            {
                var folder = Path.GetDirectoryName(file.InProject)!.Replace('\\', '/');
                var kind = KindOf(project, type);
                if (kind == null) { violations.Add($"{file.Relative}: {NameOf(type)} is a controller of no known kind"); continue; }
                var expected = "Controllers/" + kind;
                if (folder != expected) violations.Add($"{file.Relative}: {NameOf(type)} is a {kind} controller and belongs in {expected}");
            }
        }

        Assert.True(violations.Count == 0, Describe(violations));
    }

    /// <summary>
    /// The sub-folder of <c>Controllers</c> a controller belongs in: "FrontEnd" the screens (controllers returning a view), "UI" the <c>/api/ui</c> JSON twins,
    /// "Base" the abstract bases, "Api" the <c>/api/v1</c> administration REST controllers, "Protocols" the package-protocol controllers.
    /// </summary>
    private static string? KindOf(string project, ClassDeclarationSyntax type)
    {
        var name = type.Identifier.Text;
        if (type.Modifiers.Any(m => m.Text == "abstract")) return "Base";
        if (project == "BaGet.Validation") return "Protocols";

        // Twins and /api/v1 controllers both end with "ApiController"; their base tells them apart: a twin derives from the
        // ApiControllerBase family (ApiControllerBase, AccountApiControllerBase, AdminApiControllerBase, AccessApiControllerBase), an
        // /api/v1 controller from AdminApiController or AccessApiController.
        var bases = type.BaseList?.Types.Select(t => t.Type.ToString()).ToList() ?? new List<string>();
        if (name.EndsWith("ApiController", StringComparison.Ordinal) && bases.Any(b => b.EndsWith("ApiControllerBase", StringComparison.Ordinal))) return "UI";
        if (bases.Any(b => b.EndsWith("ApiController", StringComparison.Ordinal))) return "Api";
        if (bases.Any(b => b == "UiControllerBase" || (b.EndsWith("ControllerBase", StringComparison.Ordinal) && b != "ControllerBase"))) return "FrontEnd";
        if (bases.Any(b => b == "Controller" || b == "ControllerBase")) return "Protocols";

        return null;
    }

    private static bool IsType(SyntaxNode n) => n is BaseTypeDeclarationSyntax || n is DelegateDeclarationSyntax;

    private static IEnumerable<SyntaxNode> TopLevelTypes(SyntaxNode root)
        => root.DescendantNodes(n => !(n is BaseTypeDeclarationSyntax)).Where(IsType);

    private static string NameOf(SyntaxNode n) => n switch
    {
        BaseTypeDeclarationSyntax b => b.Identifier.Text,
        DelegateDeclarationSyntax d => d.Identifier.Text,
        _ => "?",
    };

    private static string Describe(List<string> violations)
        => violations.Count == 0 ? string.Empty : violations.Count + " violation(s):\n" + string.Join("\n", violations.Take(60));

    private sealed record SourceFile(string Path, string Relative, string InProject, SyntaxNode Root);

    private static IEnumerable<SourceFile> Files(string project)
    {
        var projectDirectory = Path.Combine(SourceRoot(), project);
        foreach (var path in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var inProject = Path.GetRelativePath(projectDirectory, path).Replace('\\', '/');
            if (inProject.StartsWith("obj/") || inProject.StartsWith("bin/")) continue;

            yield return new SourceFile(path, project + "/" + inProject, inProject, CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot());
        }
    }

    private static string RootNamespaceOf(string project)
    {
        var match = RootNamespace.Match(File.ReadAllText(Path.Combine(SourceRoot(), project, project + ".csproj")));
        Assert.True(match.Success, project + " declares no RootNamespace");

        return match.Groups[1].Value;
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BaGet.sln"))) directory = directory.Parent;

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("BaGet.sln not found above the test output."), "src");
    }
}
