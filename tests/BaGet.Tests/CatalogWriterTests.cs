using System;
using BaGet.Catalog.Storage;
using Xunit;

namespace BaGet.Tests;

public class CatalogWriterTests
{
    private static readonly DateTime Now = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheFirstCommitIsNow()
    {
        Assert.Equal(Now, CatalogWriter.NextTimestamp(Now, last: null));
    }

    [Fact]
    public void TruncatesToMilliseconds()
    {
        // Every provider stores a millisecond exactly; not every one stores a tick.
        var next = CatalogWriter.NextTimestamp(Now.AddTicks(12345), last: null);

        Assert.Equal(Now.AddMilliseconds(1), next);
        Assert.Equal(DateTimeKind.Utc, next.Kind);
    }

    [Fact]
    public void IsLaterThanTheLastCommitWhenTheClockHasNotMoved()
    {
        Assert.Equal(Now.AddMilliseconds(1), CatalogWriter.NextTimestamp(Now, last: Now));
    }

    [Fact]
    public void IsLaterThanTheLastCommitWhenTheClockWentBack()
    {
        var last = Now.AddMinutes(5);

        Assert.Equal(last.AddMilliseconds(1), CatalogWriter.NextTimestamp(Now, last));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(500, 1)]
    [InlineData(501, 2)]
    public void PageCountRoundsUp(int entries, int pages)
    {
        Assert.Equal(pages, CatalogReader.PageCount(entries));
    }
}