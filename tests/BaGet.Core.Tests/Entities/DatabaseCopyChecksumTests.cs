using Xunit;

namespace BaGet.Core.Tests.Entities;

public class DatabaseCopyChecksumTests
{
    private static string Of(params string[] keys)
    {
        using var checksum = new DatabaseTable.Checksum();
        foreach (var key in keys) checksum.Add(key);
        return checksum.Finish();
    }

    [Fact]
    public void DoesNotDependOnTheOrderTheKeysAreRead()
    {
        // BINARY (SQLite) and a case-insensitive collation order mixed-case keys differently.
        Assert.Equal(Of("AAAb", "AAAa", "aaaC"), Of("aaaC", "AAAa", "AAAb"));
    }

    [Fact]
    public void DiffersWhenAKeyOrTheCountDiffers()
    {
        Assert.NotEqual(Of("a", "b"), Of("a", "c"));
        Assert.NotEqual(Of("a"), Of("a", "a"));
        Assert.NotEqual(Of("a"), Of());
    }
}
