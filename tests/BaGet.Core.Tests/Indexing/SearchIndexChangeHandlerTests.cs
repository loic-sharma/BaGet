using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Indexing;

public class SearchIndexChangeHandlerTests
{
    private sealed class RecordingIndexer : ISearchIndexer
    {
        public List<string?> Ids { get; } = new();

        public Task IndexAsync(Package package, CancellationToken cancellationToken)
        {
            Ids.Add(package.Id);

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AnUnlistRelistOrDeleteReindexesTheIdSoAnExternalIndexDoesNotGoStale()
    {
        var indexer = new RecordingIndexer();
        var handler = new SearchIndexChangeHandler(indexer);
        var version = NuGetVersion.Parse("1.0.0");

        await handler.OnPackageChangedAsync("Acme", version, TestContext.Current.CancellationToken);
        await handler.OnPackageDeletedAsync("Acme", version, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "Acme", "Acme" }, indexer.Ids);
    }

    [Fact]
    public async Task TheDefaultNullIndexerAndAMissingIdAreLeftAlone()
    {
        var indexer = new RecordingIndexer();
        var handler = new SearchIndexChangeHandler(indexer);

        await handler.OnPackageChangedAsync(null, NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken);
        await new SearchIndexChangeHandler(new NullSearchIndexer()).OnPackageChangedAsync("Acme", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken);

        Assert.Empty(indexer.Ids);
    }
}
