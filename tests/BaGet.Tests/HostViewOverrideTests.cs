using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// A host that embeds BaGet overrides the layout, <c>_Avatar</c> or <c>_ScanFindings</c> by shipping its own copy; that works
/// only while BaGet's views name them (<c>Layout = "_Layout"</c>, <c>&lt;partial name="_Avatar"&gt;</c>) and let the view engine
/// search the host first. A path like <c>~/Views/Shared/_Layout.cshtml</c> would bypass a host's <c>Pages/Shared</c> copy.
/// BaGet ships no <c>Pages/</c> folder, so no file may name the absolute path.
/// </summary>
public class HostViewOverrideTests
{
    [Fact]
    public void ViewsNameTheOverridableLayoutAndPartialsByNameNotByPath()
    {
        var src = Path.Combine(AdminApiAccessTests.RepositoryRoot(), "src");
        var pattern = new Regex(@"~/Views/Shared/_(Layout|Avatar|ScanFindings)\.cshtml");
        var offenders = Directory.EnumerateFiles(src, "*.cshtml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => pattern.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(src, f))
            .ToList();

        Assert.True(offenders.Count == 0, "Name the layout/partial (_Layout, _Avatar, _ScanFindings) so a host override is found: " + string.Join(", ", offenders));
    }
}
