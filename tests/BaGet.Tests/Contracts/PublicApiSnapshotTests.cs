using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BaGet.Frontend.Controllers.Base;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Extensions;
using Xunit;

namespace BaGet.Tests.Contracts;

/// <summary>
/// The public-API contract of the web UI: the public and protected surface
/// of <c>BaGet.Web.V3</c>, <c>BaGet.Identity.Api</c>, <c>BaGet.Api.Base</c>, <c>BaGet.Frontend.Base</c>, <c>BaGet.Identity.FrontEnd</c> and <c>BaGet.Web.FrontEnd</c> (and of the Application projects their types moved to) is listed in
/// <c>Contracts/public-api.txt</c>, one line per type, base type, interface and member. The actual surface must be a <b>superset</b> of
/// the snapshot: members and types may be added, none may be removed or change signature (a removal is a breaking change: regenerate the
/// snapshot on purpose). Razor-generated view classes are not public API.
/// <para>
/// A type is identified by namespace and name, <b>not by assembly</b>: a type that moved to <c>BaGet.Identity.Application</c> or
/// <c>BaGet.Web.Application</c> still counts, provided the assembly that listed it keeps a <c>[TypeForwardedTo]</c> for it
/// (<see cref="EveryTypeOfTheSnapshotStillResolvesThroughItsOriginalAssembly"/>), so binaries compiled against the old assembly keep working.
/// </para>
/// </summary>
public class PublicApiSnapshotTests
{
    private const string FileName = "public-api.txt";

    private static readonly Assembly[] Assemblies =
    {
        typeof(BaGet.Web.V3.Routing.Routes).Assembly,
        typeof(AccountsServiceCollectionExtensions).Assembly,
        typeof(BaGet.Frontend.Mvc.UI.UiApiAttribute).Assembly,
        typeof(UiControllerBase).Assembly,
        typeof(BaGet.Identity.Controllers.FrontEnd.AdminUsersController).Assembly,
        typeof(BaGet.Web.V3.Controllers.FrontEnd.HomeController).Assembly,
        typeof(ScreenGate).Assembly,
        typeof(BaGet.Web.Application.UseCases.LegalUseCase).Assembly,
    };

    // The assemblies the snapshot was taken from: a type listed under one of them must still resolve through it (moved types are forwarded).
    private static readonly Dictionary<string, Assembly> Original = new(StringComparer.Ordinal)
    {
        ["BaGet.Web.V3"] = typeof(BaGet.Web.V3.Routing.Routes).Assembly,
        ["BaGet.Identity.Api"] = typeof(AccountsServiceCollectionExtensions).Assembly,
        ["BaGet.Api.Base"] = typeof(BaGet.Frontend.Mvc.UI.UiApiAttribute).Assembly,
        ["BaGet.Frontend.Base"] = typeof(UiControllerBase).Assembly,
        ["BaGet.Identity.FrontEnd"] = typeof(BaGet.Identity.Controllers.FrontEnd.AdminUsersController).Assembly,
        ["BaGet.Web.FrontEnd"] = typeof(BaGet.Web.V3.Controllers.FrontEnd.HomeController).Assembly,
    };

    /// <summary>The assembly column of a snapshot line is dropped: a moved type is the same line.</summary>
    private static string WithoutAssembly(string line) => line.Substring(line.IndexOf(' ') + 1);

    [Fact]
    public void ThePublicApiIsASupersetOfTheSnapshot()
    {
        var actual = Describe();

        if (Environment.GetEnvironmentVariable(ContractSnapshot.UpdateVariable) == "1")
        {
            File.WriteAllText(Path.Combine(ContractSnapshot.Folder, FileName), string.Join("\n", actual) + "\n", new UTF8Encoding(false));
            return;
        }

        var path = Path.Combine(ContractSnapshot.Folder, FileName);
        Assert.True(File.Exists(path), $"{FileName} does not exist; generate it with {ContractSnapshot.UpdateVariable}=1.");

        var expected = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        var present = new HashSet<string>(actual.Select(WithoutAssembly), StringComparer.Ordinal);
        var missing = expected.Where(l => !present.Contains(WithoutAssembly(l))).ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} public API entries of the snapshot are gone or changed (a removal is a breaking change):{Environment.NewLine}  "
            + string.Join(Environment.NewLine + "  ", missing.Take(40)));
    }

    [Fact]
    public void EveryTypeOfTheSnapshotStillResolvesThroughItsOriginalAssembly()
    {
        var path = Path.Combine(ContractSnapshot.Folder, FileName);
        var unresolved = new List<string>();

        foreach (var line in File.ReadAllLines(path).Where(l => l.Contains(" type ", StringComparison.Ordinal)))
        {
            // "<assembly> type <full name> [kind]"
            var parts = line.Split(' ');
            if (!Original.TryGetValue(parts[0], out var assembly)) continue;

            // Assembly.GetType follows a [TypeForwardedTo] to the assembly the type moved to.
            if (!Resolves(assembly, parts[2])) unresolved.Add(line);
        }

        Assert.True(unresolved.Count == 0, "Types that no longer resolve through the assembly that listed them (a moved type needs [TypeForwardedTo]):" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", unresolved.Take(40)));
    }

    // The snapshot writes a nested type as Outer.Inner; the runtime name is Outer+Inner.
    private static bool Resolves(Assembly assembly, string name)
    {
        for (var candidate = name; ; )
        {
            if (assembly.GetType(candidate, throwOnError: false) != null) return true;

            var dot = candidate.LastIndexOf('.');
            if (dot < 0) return false;

            candidate = candidate.Substring(0, dot) + "+" + candidate.Substring(dot + 1);
            if (candidate.IndexOf('.') < 0) return false;
        }
    }

    /// <summary>One sorted line per public type, base type, interface and public or protected member.</summary>
    internal static IReadOnlyList<string> Describe()
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var lines = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var assembly in Assemblies)
        {
            var assemblyName = assembly.GetName().Name;
            foreach (var type in assembly.GetTypes().Where(IsPublicApi))
            {
                var name = Name(type);
                lines.Add($"{assemblyName} type {name} [{Kind(type)}]");
                if (type.BaseType != null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType) && type.BaseType != typeof(Enum))
                {
                    lines.Add($"{assemblyName} {name} extends {Name(type.BaseType)}");
                }

                foreach (var contract in type.GetInterfaces()) lines.Add($"{assemblyName} {name} implements {Name(contract)}");

                foreach (var member in type.GetMembers(Flags))
                {
                    var signature = Signature(member);
                    if (signature != null) lines.Add($"{assemblyName} {name}::{signature}");
                }
            }
        }

        return lines.ToList();
    }

    private static bool IsPublicApi(Type type)
        => (type.IsPublic || (type.IsNestedPublic && IsPublicApi(type.DeclaringType!)))
           && type.Namespace?.StartsWith("AspNetCoreGeneratedDocument", StringComparison.Ordinal) != true
           && !type.Name.StartsWith("<", StringComparison.Ordinal)
           && type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() == null;

    private static string Kind(Type type)
        => type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsSealed && type.IsAbstract ? "static class" : type.IsAbstract ? "abstract class" : type.IsSealed ? "sealed class" : "class";

    private static string Name(Type type)
    {
        if (type.IsGenericParameter) return type.Name;
        if (type.IsArray) return Name(type.GetElementType()!) + "[]";
        if (type.IsByRef) return "ref " + Name(type.GetElementType()!);
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var definition = type.GetGenericTypeDefinition();
            return Name(definition).Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(Name)) + ">";
        }

        return (type.FullName ?? type.Name).Replace('+', '.');
    }

    private static bool Visible(MethodBase? method) => method != null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

    private static string Parameters(MethodBase method)
        => string.Join(", ", method.GetParameters().Select(p => $"{Name(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? " = " + (p.DefaultValue ?? "null") : string.Empty)}"));

    private static string? Signature(MemberInfo member)
    {
        switch (member)
        {
            case ConstructorInfo constructor when Visible(constructor):
                return $"ctor({Parameters(constructor)})";
            case MethodInfo method when Visible(method) && !method.IsSpecialName:
                return $"{(method.IsStatic ? "static " : string.Empty)}{(method.IsVirtual && !method.IsFinal ? "virtual " : string.Empty)}{Name(method.ReturnType)} {method.Name}{(method.IsGenericMethodDefinition ? "<" + string.Join(",", method.GetGenericArguments().Select(a => a.Name)) + ">" : string.Empty)}({Parameters(method)})";
            case PropertyInfo property when Visible(property.GetMethod) || Visible(property.SetMethod):
                var get = Visible(property.GetMethod) ? "get;" : string.Empty;
                var set = Visible(property.SetMethod) ? (property.SetMethod!.IsPublic || !Visible(property.GetMethod) || property.GetMethod!.IsPublic ? "set;" : "protected set;") : string.Empty;
                return $"{(property.GetMethod?.IsStatic == true ? "static " : string.Empty)}{Name(property.PropertyType)} {property.Name} {{ {get} {set} }}".Replace("  ", " ");
            case FieldInfo field when field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly:
                return $"{(field.IsStatic ? "static " : string.Empty)}{(field.IsLiteral ? "const " : string.Empty)}{Name(field.FieldType)} {field.Name}";
            case EventInfo @event when Visible(@event.AddMethod):
                return $"event {Name(@event.EventHandlerType!)} {@event.Name}";
            default:
                return null;
        }
    }
}
