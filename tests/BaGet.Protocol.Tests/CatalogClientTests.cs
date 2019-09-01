using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class CatalogClientTests : IClassFixture<ProtocolFixture>
    {
        private readonly CatalogClient _target;

        public CatalogClientTests(ProtocolFixture fixture)
        {
            _target = fixture.CatalogClient;
        }

        [Fact]
        public async Task GetsCatalogIndex()
        {
            var result = await _target.GetIndexAsync();

            Assert.NotNull(result);
            Assert.True(result.Count > 0);
            Assert.NotEmpty(result.Items);
        }

        [Fact]
        public async Task GetsCatalogPages()
        {
            // TODO: Replace with a single page test.
            var index = await _target.GetIndexAsync();

            var work = new ConcurrentBag<CatalogPageItem>(index.Items);
            var results = new ConcurrentBag<CatalogPage>();

            var tasks = Enumerable
                .Repeat(0, 32)
                .Select(async _ =>
                {
                    while (work.TryTake(out var pageItem))
                    {
                        var page = await _target.GetPageAsync(pageItem.Url);

                        results.Add(page);
                    }
                });

            await Task.WhenAll(tasks);
        }
    }
}
