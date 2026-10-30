using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace BaGet.Tests.Contracts;

/// <summary>
/// Compares generated text with a committed snapshot under <c>tests/BaGet.Tests/Contracts/</c>. The snapshots are the
/// definition of "not breaking" for the web UI (routes, forms, public API): a change never edits them to make a test pass. Set the environment variable
/// <c>BAGET_UPDATE_CONTRACTS=1</c> to (re)write a snapshot, which is only legitimate for a deliberate, reviewed contract change.
/// </summary>
internal static class ContractSnapshot
{
    public const string UpdateVariable = "BAGET_UPDATE_CONTRACTS";

    /// <summary>The folder of the snapshot files in the source tree (not the test output folder).</summary>
    public static string Folder => Path.Combine(AdminApiAccessTests.RepositoryRoot(), "tests", "BaGet.Tests", "Contracts");

    /// <summary>Asserts that <paramref name="actual"/> equals the snapshot file byte for byte (LF line ends, UTF-8 without BOM).</summary>
    public static void Verify(string fileName, string actual)
    {
        var path = Path.Combine(Folder, fileName);
        actual = Normalize(actual);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            return;
        }

        Assert.True(File.Exists(path), $"{fileName} does not exist; generate it with {UpdateVariable}=1.");
        var expected = Normalize(File.ReadAllText(path));
        if (expected == actual) return;

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var removed = expectedLines.Except(actualLines).Take(15).ToList();
        var added = actualLines.Except(expectedLines).Take(15).ToList();
        Assert.Fail($"{fileName} changed (the UI contract must stay byte-identical).{Environment.NewLine}"
            + $"Missing ({expectedLines.Except(actualLines).Count()}):{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", removed) + Environment.NewLine
            + $"New ({actualLines.Except(expectedLines).Count()}):{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", added));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
}
