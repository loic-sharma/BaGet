using System;
using System.IO;
using Xunit;

namespace BaGet.Web.Tests;

/// <summary>Guards the golden-baguette palette: the header band is gold with dark text, not nuget.org blue.</summary>
public class SiteCssTests
{
    private static string ReadFromRepo(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    [Fact]
    public void DefinesHeaderVariablesAndNoLongerUsesNugetBlue()
    {
        var css = ReadFromRepo(Path.Combine("src", "BaGet", "wwwroot", "css", "site.css"));

        Assert.Contains("--header-bg:", css);
        Assert.Contains("--header-fg:", css);
        Assert.DoesNotContain("#004880", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogoIsVisibleAgainstTheGoldenBand()
    {
        var layout = ReadFromRepo(Path.Combine("src", "BaGet.Frontend.Base", "Views", "Shared", "_Layout.cshtml"));

        Assert.DoesNotContain("#004880", layout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fill=\"#f5a623\"", layout, StringComparison.OrdinalIgnoreCase);
    }
}
