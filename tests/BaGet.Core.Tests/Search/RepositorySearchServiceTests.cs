using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BaGet.Core.Tests.Search;

/// <summary>
/// The merged search of a virtual repository, against members that are fake searches large enough to need several pages
/// (members are read 500 results at a time).
/// </summary>
public class RepositorySearchServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static SearchResult Result(string id, long downloads = 0, params string[] versions)
        => new()
        {
            PackageId = id,
            Version = versions.LastOrDefault() ?? "1.0.0",
            TotalDownloads = downloads,
            Versions = (versions.Length == 0 ? new[] { "1.0.0" } : versions)
                .Reverse()
                .Select(v => new SearchResultVersion { Version = v, Downloads = 0 })
                .ToList(),
        };

    /// <summary>A search over a fixed, id-ordered list that honours skip and take like a database does.</summary>
    private sealed class FakeSearch : ISearchService
    {
        private readonly IRepositoryContext _repository;
        private readonly Dictionary<string, List<SearchResult>> _data;

        public FakeSearch(IRepositoryContext repository, Dictionary<string, List<SearchResult>> data)
        {
            _repository = repository;
            _data = data;
        }

        public int Calls;

        /// <summary>Every (skip, take) any fake member was asked; cleared by the test that reads it (the tests of this class run one at a time).</summary>
        public static readonly System.Collections.Concurrent.ConcurrentQueue<(int Skip, int Take)> Requests = new();

        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            Requests.Enqueue((request.Skip, request.Take));
            var all = _data[_repository.Current.Name]
                .Where(r => string.IsNullOrEmpty(request.Query) || r.PackageId!.Contains(request.Query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var ordered = request.SortBy == SearchSort.TotalDownloads
                ? all.OrderByDescending(r => r.TotalDownloads).ThenBy(r => r.PackageId, StringComparer.Ordinal)
                : all.OrderBy(r => r.PackageId, StringComparer.Ordinal);

            return Task.FromResult(new SearchResponse
            {
                TotalHits = all.Count,
                Data = ordered.Skip(request.Skip).Take(request.Take).ToList(),
            });
        }

        public Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken)
        {
            var all = _data[_repository.Current.Name].Select(r => r.PackageId!).OrderBy(i => i, StringComparer.Ordinal).ToList();

            return Task.FromResult(new AutocompleteResponse { TotalHits = all.Count, Data = all.Skip(request.Skip).Take(request.Take).ToList() });
        }

        public Task<AutocompleteResponse> ListPackageVersionsAsync(VersionsRequest request, CancellationToken cancellationToken)
        {
            var versions = _data[_repository.Current.Name]
                .Where(r => string.Equals(r.PackageId, request.PackageId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(r => r.Versions!.Select(v => v.Version))
                .ToList();

            return Task.FromResult(new AutocompleteResponse { TotalHits = versions.Count, Data = versions });
        }

        public Task<DependentsResponse> FindDependentsAsync(string? packageId, CancellationToken cancellationToken)
            => Task.FromResult(new DependentsResponse
            {
                Data = _data[_repository.Current.Name]
                    .Select(r => new PackageDependent { Id = r.PackageId, TotalDownloads = r.TotalDownloads })
                    .ToList(),
            });
    }

    private static (RepositorySearchService Search, IServiceScope Scope) Create(Dictionary<string, List<SearchResult>> data, params string[] members)
    {
        var infos = members.Select((name, i) => new RepositoryInfo(i + 2, name, RepositoryFormat.NuGet, RepositoryKind.Local)).ToList();
        var virtualInfo = new RepositoryInfo(99, "v", RepositoryFormat.NuGet, RepositoryKind.Virtual);

        var virtuals = new Mock<IVirtualRepositories>();
        virtuals.Setup(v => v.EffectiveMembersAsync(It.IsAny<RepositoryInfo>(), It.IsAny<CancellationToken>())).ReturnsAsync(infos);
        var evaluator = new Mock<IAccessEvaluator>();
        evaluator
            .Setup(e => e.IsAllowedAsync(It.IsAny<BaGetPrincipal?>(), It.IsAny<RepositoryInfo>(), It.IsAny<string?>(), It.IsAny<AccessAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRepositoryContext, RepositoryContext>();
        services.AddScoped<IRequestCaller, RequestCaller>();
        services.AddSingleton(virtuals.Object);
        services.AddSingleton(evaluator.Object);
        services.AddScoped<VirtualRepositoryReader>();
        services.AddScoped<ISearchService>(p => new FakeSearch(p.GetRequiredService<IRepositoryContext>(), data));
        services.AddSingleton<ISearchResponseBuilder>(new SearchResponseBuilder(Mock.Of<IUrlGenerator>()));

        var scope = services.BuildServiceProvider().CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use(virtualInfo);
        var search = new RepositorySearchService(
            scope.ServiceProvider.GetRequiredService<ISearchService>(),
            scope.ServiceProvider.GetRequiredService<VirtualRepositoryReader>(),
            scope.ServiceProvider.GetRequiredService<ISearchResponseBuilder>());

        return (search, scope);
    }

    private static List<SearchResult> Many(string prefix, int count, long downloads = 0)
        => Enumerable.Range(0, count).Select(i => Result($"{prefix}{i:D5}", downloads)).ToList();

    [Fact]
    public async Task MembersLargerThanOnePageAreReadCompletelyAndPagedWithoutRepeatsOrGaps()
    {
        // 1200 ids in "a", 700 in "b", 300 of them shared: 1600 distinct.
        var shared = Many("s", 300);
        var data = new Dictionary<string, List<SearchResult>>
        {
            ["a"] = Many("a", 900).Concat(shared).ToList(),
            ["b"] = Many("b", 400).Concat(shared).ToList(),
        };
        var (search, scope) = Create(data, "a", "b");
        using (scope)
        {
            var seen = new List<string>();
            for (var skip = 0; skip < 1700; skip += 250)
            {
                var page = await search.SearchAsync(new SearchRequest { Skip = skip, Take = 250 }, Cancel);
                Assert.Equal(1600, page.TotalHits);
                seen.AddRange(page.Data!.Select(d => d.PackageId!));
            }

            Assert.Equal(1600, seen.Count);
            Assert.Equal(1600, seen.Distinct().Count());
            Assert.Equal(seen.OrderBy(i => i, StringComparer.OrdinalIgnoreCase).ToList(), seen);
        }
    }

    [Fact]
    public async Task AFirstPageOfLargeMembersAsksEachOnceForSkipPlusTakePlusOneResults()
    {
        var data = new Dictionary<string, List<SearchResult>>
        {
            ["a"] = Many("a", 5000),
            ["b"] = Many("b", 3000),
        };
        var (search, scope) = Create(data, "a", "b");
        using (scope)
        {
            while (FakeSearch.Requests.TryDequeue(out _))
            {
            }

            var page = await search.SearchAsync(new SearchRequest { Skip = 0, Take = 10 }, Cancel);

            Assert.Equal(new[] { (0, 11), (0, 11) }, FakeSearch.Requests.ToArray());
            Assert.Equal(Many("a", 10).Select(r => r.PackageId), page.Data!.Select(d => d.PackageId));

            // Past the exact limit the total is an estimate: never below the distinct ids, never above the sum of the members.
            Assert.InRange(page.TotalHits, 22, 8000);
        }
    }

    [Fact]
    public async Task ADeepPageReadsEachMemberOnlyAsFarAsItNeedsAndNeverBeyondTheCap()
    {
        var data = new Dictionary<string, List<SearchResult>>
        {
            ["a"] = Many("a", 12000),
            ["b"] = Many("b", 12000),
        };
        var (search, scope) = Create(data, "a", "b");
        using (scope)
        {
            while (FakeSearch.Requests.TryDequeue(out _))
            {
            }

            // The merge interleaves a and b by id: all of "a" sorts before "b", so ids 2495..2504 are all member a's.
            var page = await search.SearchAsync(new SearchRequest { Skip = 2495, Take = 10 }, Cancel);
            Assert.Equal(Many("a", 2505).Skip(2495).Select(r => r.PackageId), page.Data!.Select(d => d.PackageId));

            var requests = FakeSearch.Requests.ToArray();
            var perMember = RepositorySearchService.MaxScanPerMember / RepositorySearchService.MemberPageSize;
            Assert.True(requests.Length <= 2 * perMember, $"{requests.Length} member calls");
            Assert.Equal(2 * 6, requests.Length);
            Assert.All(requests, r => Assert.True(r.Take <= RepositorySearchService.MemberPageSize));
            Assert.Equal(2 * 2506, requests.Sum(r => r.Take));

            FakeSearch.Requests.Clear();
            await search.SearchAsync(new SearchRequest { Skip = 5_000_000, Take = 10 }, Cancel);
            Assert.True(FakeSearch.Requests.Count <= 2 * perMember);
            Assert.Equal(2 * RepositorySearchService.MaxScanPerMember, FakeSearch.Requests.Sum(r => r.Take));
        }
    }

    [Fact]
    public async Task AnIdInSeveralMembersIsOneResultWithTheFirstMembersEntryTheUnionOfVersionsAndSummedDownloads()
    {
        var data = new Dictionary<string, List<SearchResult>>
        {
            ["a"] = new() { Result("Pkg", 10, "1.0.0", "1.5.0") },
            ["b"] = new() { Result("Pkg", 5, "1.5.0", "2.0.0"), Result("Other", 1) },
        };
        var (search, scope) = Create(data, "a", "b");
        using (scope)
        {
            var response = await search.SearchAsync(new SearchRequest { Take = 10 }, Cancel);

            Assert.Equal(2, response.TotalHits);
            var pkg = response.Data!.Single(d => d.PackageId == "Pkg");
            Assert.Equal(15, pkg.TotalDownloads);
            Assert.Equal(new[] { "2.0.0", "1.5.0", "1.0.0" }, pkg.Versions!.Select(v => v.Version));
            Assert.Equal("2.0.0", pkg.Version);
        }
    }

    [Fact]
    public async Task TheDownloadsOrderIsTotalAndStableAcrossPages()
    {
        var data = new Dictionary<string, List<SearchResult>>
        {
            ["a"] = new() { Result("A", 5), Result("B", 50), Result("C", 5) },
            ["b"] = new() { Result("B", 10), Result("D", 60), Result("E", 5) },
        };
        var (search, scope) = Create(data, "a", "b");
        using (scope)
        {
            var ids = new List<string>();
            for (var skip = 0; skip < 5; skip += 2)
            {
                var page = await search.SearchAsync(new SearchRequest { Skip = skip, Take = 2, SortBy = SearchSort.TotalDownloads }, Cancel);
                ids.AddRange(page.Data!.Select(d => d.PackageId!));
            }

            // B = 60 (summed), D = 60: equal downloads fall back to the id; then 5-download ones by id.
            Assert.Equal(new[] { "B", "D", "A", "C", "E" }, ids);
            Assert.Equal(new[] { SearchSort.Relevance, SearchSort.TotalDownloads }, search.SupportedSorts);
        }
    }

    [Fact]
    public async Task AutocompleteDedupesOrdersAndPagesTheMergedIds()
    {
        var data = new Dictionary<string, List<SearchResult>>
        {
            ["a"] = Many("x", 700),
            ["b"] = Many("x", 100).Concat(new[] { Result("y00000") }).ToList(),
        };
        var (search, scope) = Create(data, "a", "b");
        using (scope)
        {
            var page = await search.AutocompleteAsync(new AutocompleteRequest { Skip = 698, Take = 5 }, Cancel);

            Assert.Equal(701, page.TotalHits);
            Assert.Equal(new[] { "x00698", "x00699", "y00000" }, page.Data);
        }
    }

    [Fact]
    public async Task AMemberBeyondTheScanLimitMakesTheTotalAnEstimateNotAnUnderCount()
    {
        var data = new Dictionary<string, List<SearchResult>>
        {
            ["a"] = Many("a", RepositorySearchService.MaxScanPerMember + 600),
        };
        var (search, scope) = Create(data, "a");
        using (scope)
        {
            var page = await search.SearchAsync(new SearchRequest { Skip = 0, Take = 5 }, Cancel);

            Assert.Equal(RepositorySearchService.MaxScanPerMember + 600, page.TotalHits);
            Assert.Equal(5, page.Data!.Count);
        }
    }

    [Fact]
    public async Task VersionListsAreMergedAndDependentsSummedAndCapped()
    {
        var data = new Dictionary<string, List<SearchResult>>
        {
            ["a"] = new() { Result("Pkg", 3, "1.0.0", "10.0.0"), Result("Dep", 2) },
            ["b"] = new() { Result("Pkg", 4, "2.0.0", "1.0.0") },
        };
        var (search, scope) = Create(data, "a", "b");
        using (scope)
        {
            var versions = await search.ListPackageVersionsAsync(new VersionsRequest { PackageId = "pkg" }, Cancel);
            Assert.Equal(new[] { "1.0.0", "2.0.0", "10.0.0" }, versions.Data);
            Assert.Equal(3, versions.TotalHits);

            var dependents = await search.FindDependentsAsync("x", Cancel);
            Assert.Equal(new[] { "Pkg", "Dep" }, dependents.Data!.Select(d => d.Id));
            Assert.Equal(7, dependents.Data!.First().TotalDownloads);
        }
    }

    [Fact]
    public async Task ARepositoryThatIsNotVirtualUsesTheInnerSearchUntouched()
    {
        var data = new Dictionary<string, List<SearchResult>> { ["plain"] = new() { Result("Only") } };
        var (_, scope) = Create(data, "plain");
        using (scope)
        {
            scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use(new RepositoryInfo(2, "plain", RepositoryFormat.NuGet, RepositoryKind.Local));
            var inner = scope.ServiceProvider.GetRequiredService<ISearchService>();
            var search = new RepositorySearchService(inner, scope.ServiceProvider.GetRequiredService<VirtualRepositoryReader>(), scope.ServiceProvider.GetRequiredService<ISearchResponseBuilder>());

            var response = await search.SearchAsync(new SearchRequest { Take = 10 }, Cancel);

            Assert.Equal(1, response.TotalHits);
            Assert.Equal(inner.SupportedSorts, search.SupportedSorts);
        }
    }
}
