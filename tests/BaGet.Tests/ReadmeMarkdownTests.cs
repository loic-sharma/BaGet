using Xunit;
using BaGet.Web.Application.Models.Packages;

namespace BaGet.Tests;

/// <summary>
/// A package's readme is written by whoever pushed it, and with open registration that
/// is anyone, so it is rendered as untrusted text.
/// </summary>
public class ReadmeMarkdownTests
{
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>")]
    public void RawHtmlIsEscapedNotRendered(string? markdown)
    {
        #pragma warning disable CS8602
        var html = ReadmeMarkdown.ToHtml(markdown).ToString();
        #pragma warning restore CS8602

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<iframe", html);
        Assert.DoesNotContain("<a ", html);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JaVaScRiPt:alert(1))")]
    [InlineData("![x](javascript:alert(1))")]
    [InlineData("[x](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("[x][r]\n\n[r]: javascript:alert(1)")]
    public void ScriptSchemeLinksAreNotProduced(string? markdown)
    {
        #pragma warning disable CS8602
        var html = ReadmeMarkdown.ToHtml(markdown).ToString().ToLowerInvariant();
        #pragma warning restore CS8602

        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("data:text/html", html);
    }

    [Fact]
    public void OrdinaryMarkdownStillRenders()
    {
        #pragma warning disable CS8602
        var html = ReadmeMarkdown.ToHtml("# Title\n\n**bold** [site](https://example.com)\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n```csharp\nvar x = 1;\n```").ToString();
        #pragma warning restore CS8602

        Assert.Contains("<h1", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("href=\"https://example.com\"", html);
        Assert.Contains("<table", html);
        Assert.Contains("<code", html);
    }

    // --- attribute injection: "{onmouseover=...}" must never become an attribute ----------

    public static TheoryData<string> AttributePayloads => new()
    {
        "# Hi {onmouseover=\"alert(1)\"}",
        "[x](https://a.example){onclick=alert(1)}",
        "[x](https://a.example){ONCLICK=alert(1)}",
        "[x](https://a.example){OnClick='alert(1)'}",
        "# Hi {on&#109;ouseover=\"alert(1)\"}",
        "# Hi {&#111;nmouseover=alert(1)}",
        "![x](https://a.example/i.png){onerror=alert(1)}",
        "text {onfocus=alert(1) autofocus}",
        "# Hi {style=\"background:url(javascript:alert(1))\"}",
        "[x](https://a.example){formaction=javascript:alert(1)}",
        "[x](https://a.example){href=javascript:alert(1)}",
        "::: {onclick=alert(1)}\ntext\n:::",
    };

    [Theory]
    [MemberData(nameof(AttributePayloads))]
    public void AttributeSyntaxNeverEmitsAnEventHandlerOrStyle(string? markdown)
    {
        #pragma warning disable CS8602
        var html = ReadmeMarkdown.ToHtml(markdown).ToString();
        var legal = ReadmeMarkdown.ToSafeHtml(markdown).ToString();
        #pragma warning restore CS8602

        foreach (var output in new[] { html, legal })
        {
            AssertNoUnsafeAttribute(output);
        }
    }

    [Theory]
    [MemberData(nameof(AttributePayloads))]
    public void TheSanitiserHoldsEvenIfGenericAttributesWereEnabled(string? markdown)
    {
        #pragma warning disable CS8602
        var html = ReadmeMarkdown.ToHtml(markdown, ReadmeMarkdown.CreatePipeline(genericAttributes: true)).ToString();
        #pragma warning restore CS8602

        AssertNoUnsafeAttribute(html);
    }

    [Fact]
    public void TheSanitiserKeepsHarmlessAttributes()
    {
        #pragma warning disable CS8602
        var html = ReadmeMarkdown.ToHtml("# Hi {#anchor .big}", ReadmeMarkdown.CreatePipeline(genericAttributes: true)).ToString();
        #pragma warning restore CS8602

        Assert.Contains("id=\"anchor\"", html);
        Assert.Contains("class=\"big\"", html);
    }

    private static void AssertNoUnsafeAttribute(string html)
    {
        var lowered = html.ToLowerInvariant();
        Assert.DoesNotMatch(@"<[^>]*\son[a-z]+\s*=", lowered);
        Assert.DoesNotMatch(@"<[^>]*\sstyle\s*=", lowered);
        Assert.DoesNotMatch(@"<[^>]*\sformaction\s*=", lowered);
        Assert.DoesNotMatch(@"<[^>]*javascript:", lowered);
    }
}
