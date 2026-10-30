using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Jint;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The site ships Alpine 2, which turns every directive into JavaScript with <c>new Function</c>:
/// <c>x-data</c>, <c>x-init</c>, <c>x-bind</c>, <c>x-show</c>, <c>x-text</c> as
/// <c>__alpine_result = EXPRESSION</c>, and <c>x-on</c>/<c>@event</c> as statements. A directive
/// that does not parse there throws in the browser and takes the whole component down, which no
/// markup-only assertion notices. These tests take the directives out of the rendered pages and
/// compile them the way Alpine 2 does (wwwroot/lib/alpinejs/dist/alpine.js, saferEval and
/// saferEvalNoReturn).
/// </summary>
public class AlpineExpressionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public AlpineExpressionTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private (BaGetApplication App, Browser Browser) Start(Dictionary<string, string>? settings = null)
    {
        var app = new BaGetApplication(_output, configOverrides: settings);
        var browser = new Browser(app);
        _disposables.Add(app);
        _disposables.Add(browser);

        return (app, browser);
    }

    private static async Task AddAsync(BaGetApplication app, string id)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        #pragma warning disable CS8602
        context.Packages.Add(new Package
        {
            Id = id,
            Version = NuGetVersion.Parse("1.0.0"),
            Authors = new[] { "An Author" },
            Description = "Description",
            Listed = true,
            SemVerLevel = SemVerLevel.Unknown,
            Published = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Summary = "Summary",
            Tags = Array.Empty<string>(),
            Dependencies = new List<PackageDependency>(),
            PackageTypes = new List<PackageType>(),
            TargetFrameworks = new List<TargetFramework> { new() { Moniker = "net8.0" } },
        });
        #pragma warning restore CS8602
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static readonly Regex Directive = new(
        @"\s(?<name>x-[\w:.\-]+|@[\w:.\-]+|:[\w\-]+)=""(?<value>[^""]*)""",
        RegexOptions.Compiled);

    private enum Kind { None, Expression, Statements }

    private static Kind KindOf(string name)
    {
        if (name.StartsWith("@") || name.StartsWith("x-on:")) return Kind.Statements;

        var bare = name.Split('.')[0];
        if (bare == "x-data" || bare == "x-init" || bare == "x-show" || bare == "x-text" || bare == "x-html"
            || bare == "x-if" || bare == "x-bind" || bare.StartsWith("x-bind:") || bare.StartsWith(":"))
        {
            return Kind.Expression;
        }

        // x-for ("item in items"), x-model, x-ref, x-cloak, x-transition...: no plain expression.
        return Kind.None;
    }

    private static string Wrap(Kind kind, string expression)
        => kind == Kind.Expression
            ? $"function f($data){{ var __alpine_result; with($data) {{ __alpine_result = {expression} }}; return __alpine_result }}"
            : $"async function f($data, $event){{ with($data) {{ {expression} }} }}";

    private static List<(string Name, string Expression)> DirectivesIn(string html)
        => Directive.Matches(html)
            .Select(m => (m.Groups["name"].Value, WebUtility.HtmlDecode(m.Groups["value"].Value)))
            .Where(d => KindOf(d.Item1) != Kind.None)
            .ToList();

    private static void AssertAllCompile(string page, string html)
    {
        var directives = DirectivesIn(html);
        foreach (var (name, expression) in directives)
        {
            try
            {
                new Acornima.Parser().ParseScript(Wrap(KindOf(name), expression));
            }
            catch (Exception e)
            {
                Assert.Fail($"{page}: {name}=\"{expression}\" does not compile the way Alpine 2 builds it: {e.Message}");
            }
        }
    }

    [Fact]
    public void TheCheckRejectsAStatementInXInit()
    {
        // The shape that once shipped on the package page: a declaration is not an expression.
        var broken = "<div x-data=\"{ tab: 'a' }\" x-init=\"const t = location.hash; if (t) tab = t\"></div>";

        Assert.ThrowsAny<Exception>(() => AssertAllCompile("broken", broken));
    }

    [Fact]
    public async Task EveryDirectiveOnThePackagePageCompiles()
    {
        var (app, browser) = Start();
        await AddAsync(app, "Alpine.Pkg");

        var html = await browser.GetPageAsync("packages/Alpine.Pkg");

        Assert.NotEmpty(DirectivesIn(html));
        AssertAllCompile("package", html);
    }

    [Fact]
    public async Task EveryDirectiveOnTheUploadPageCompiles()
    {
        var (_, browser) = Start();

        var html = await browser.GetPageAsync("upload");

        Assert.NotEmpty(DirectivesIn(html));
        AssertAllCompile("upload", html);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("?q=Alpine", false)]
    [InlineData("", true)]
    [InlineData("?q=Alpine", true)]
    public async Task EveryDirectiveOnTheIndexAndTheLayoutCompiles(string query, bool accounts)
    {
        var settings = accounts ? new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } } : null;
        var (app, browser) = Start(settings);
        await AddAsync(app, "Alpine.Pkg");

        var html = await browser.GetPageAsync(query);

        AssertAllCompile("index" + query, html);
    }

    [Theory]
    [InlineData("", "readme")]
    [InlineData("#tab-versions", "versions")]
    [InlineData("#tab-releasenotes", "releasenotes")]
    [InlineData("#tab-nope", "readme")]
    [InlineData("#tab-'])+alert(1)+(['", "readme")]
    [InlineData("#install-1", "readme")]
    public async Task TheTabComponentSelectsTheTabNamedByTheHash(string hash, string expected)
    {
        var (app, browser) = Start();
        await AddAsync(app, "Alpine.Pkg");
        var html = await browser.GetPageAsync("packages/Alpine.Pkg");
        var tabs = DirectivesIn(html);
        var data = tabs.Single(d => d.Name == "x-data" && d.Expression.Contains("selectFromHash")).Expression;
        var init = tabs.Single(d => d.Name == "x-init" && d.Expression.Contains("selectFromHash")).Expression;

        // Alpine 2: the data object is built first, then x-init runs with it as the scope.
        var engine = new Engine();
        engine.Execute("var location = { hash: " + System.Text.Json.JsonSerializer.Serialize(hash) + " };");
        engine.Execute("var $data = (" + data + ");");
        engine.Execute("with ($data) { " + init + " }");

        Assert.Equal(expected, engine.Evaluate("$data.tab").AsString());
    }
}
