using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Tests <see cref="DatabaseSearchService"/> against a real SQLite database, so that the
/// queries under test are the ones a running BaGet would execute.
/// </summary>
public class DatabaseSearchServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteContext _context;
    private readonly DatabaseSearchService _target;
    private readonly CancellationToken _cancellationToken = CancellationToken.None;

    public DatabaseSearchServiceTests()
    {
        // An in-memory SQLite database lives as long as its connection, so the connection
        // is held open for the lifetime of the test.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new SqliteContext(options);
        _context.Database.Migrate();

        _target = new DatabaseSearchService(
            _context,
            new FrameworkCompatibilityService(),
            new SearchResponseBuilder(Mock.Of<IUrlGenerator>()));
    }

    public class SortBy : DatabaseSearchServiceTests
    {
        private SearchRequest Sorted(SearchSort sort, int skip = 0, int take = 20)
        {
            var request = Request(skip, take);
            request.SortBy = sort;
            return request;
        }

        [Fact]
        public void TheDatabaseProviderSupportsEverySort()
        {
            Assert.Equal(
                new[] { SearchSort.Relevance, SearchSort.TotalDownloads, SearchSort.LastUpdated },
                ((ISearchService)_target).SupportedSorts.OrderBy(s => s));
        }

        [Fact]
        public async Task TotalDownloadsOrdersByTheSumOverAllVersions()
        {
            await AddPackageAsync("A.Few", "1.0.0", downloads: 10);
            await AddPackageAsync("B.Many", "1.0.0", downloads: 300);
            await AddPackageAsync("B.Many", "2.0.0", downloads: 300);
            await AddPackageAsync("C.Some", "1.0.0", downloads: 100);

            var response = await _target.SearchAsync(Sorted(SearchSort.TotalDownloads), _cancellationToken);

            #pragma warning disable CS8604
            Assert.Equal(new[] { "B.Many", "C.Some", "A.Few" }, response.Data.Select(r => r.PackageId));
            #pragma warning restore CS8604
        }

        [Fact]
        public async Task LastUpdatedOrdersByTheNewestVersionFirst()
        {
            await AddPackageAsync("A.Old", "1.0.0", published: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            await AddPackageAsync("B.Fresh", "1.0.0", published: new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            await AddPackageAsync("B.Fresh", "2.0.0", published: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            await AddPackageAsync("C.Mid", "1.0.0", published: new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var response = await _target.SearchAsync(Sorted(SearchSort.LastUpdated), _cancellationToken);

            #pragma warning disable CS8604
            Assert.Equal(new[] { "B.Fresh", "C.Mid", "A.Old" }, response.Data.Select(r => r.PackageId));
            #pragma warning restore CS8604
        }

        [Theory]
        [InlineData(false, new[] { "B", "A" }, new[] { 2023, 2021 })]
        [InlineData(true, new[] { "A", "B" }, new[] { 2024, 2023 })]
        public async Task TheLastUpdatedDateOfAResultAgreesWithTheOrderUnderTheSameFilters(
            bool prerelease, string[] expectedOrder, int[] expectedYears)
        {
            await AddPackageAsync("A", "1.0.0", published: new DateTime(2021, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            await AddPackageAsync("A", "2.0.0-beta", published: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            await AddPackageAsync("B", "1.0.0", published: new DateTime(2023, 6, 1, 0, 0, 0, DateTimeKind.Utc));

            var request = Sorted(SearchSort.LastUpdated);
            request.IncludePrerelease = prerelease;
            request.IncludeSemVer2 = true;
            var response = await _target.SearchAsync(request, _cancellationToken);
            #pragma warning disable CS8604
            var ids = response.Data.Select(r => r.PackageId).ToList();
            #pragma warning restore CS8604

            var dates = await new StatisticsService(_context).GetLastUpdatedAsync(
                ids, _cancellationToken, includePrerelease: prerelease, includeSemVer2: true);

            Assert.Equal(expectedOrder, ids);
            #pragma warning disable CS8604
            Assert.Equal(expectedYears, ids.Select(id => dates[id].Year));
            #pragma warning restore CS8604
        }

        [Fact]
        public async Task TiesAreBrokenByIdSoPagesNeverOverlapOrSkip()
        {
            // Inserted in reverse key order, all tied, so neither insertion order nor row order
            // happens to equal key order, and paged so the database must apply a limit and offset.
            var ids = Enumerable.Range(0, 30).Select(i => "P" + i.ToString("00")).ToList();
            foreach (var id in Enumerable.Reverse(ids))
            {
                await AddPackageAsync(id, "1.0.0", downloads: 7);
            }

            var paged = new List<string>();
            for (var skip = 0; skip < ids.Count; skip += 7)
            {
                var page = await _target.SearchAsync(Sorted(SearchSort.TotalDownloads, skip, take: 7), _cancellationToken);
                #pragma warning disable CS8604, CS8620
                paged.AddRange(page.Data.Select(r => r.PackageId));
                #pragma warning restore CS8604, CS8620
                Assert.Equal(ids.Count, page.TotalHits);
            }

            Assert.Equal(ids, paged);
        }

        // SQLite happens to return tied groups in key order, so the paging test above cannot fail
        // there. Other providers make no such promise, so assert the tie-breaker in the SQL itself.
        [Theory]
        [InlineData(SearchSort.TotalDownloads)]
        [InlineData(SearchSort.LastUpdated)]
        public void EverySortEndsInThePackageIdInTheGeneratedQuery(SearchSort sort)
        {
            var sql = DatabaseSearchService.OrderPackageIds(_context.Packages, sort).ToQueryString();

            var orderBy = sql.Substring(sql.IndexOf("ORDER BY", StringComparison.Ordinal));
            Assert.Matches(@"ORDER BY .+ DESC, ""p""\.""Id""", orderBy);
        }

        [Fact]
        public async Task SortingAcrossPagesIsGlobalNotPerPage()
        {
            await AddPackageAsync("A", "1.0.0", downloads: 1);
            await AddPackageAsync("B", "1.0.0", downloads: 2);
            await AddPackageAsync("C", "1.0.0", downloads: 3);

            var first = await _target.SearchAsync(Sorted(SearchSort.TotalDownloads, take: 1), _cancellationToken);

            #pragma warning disable CS8604
            Assert.Equal("C", Assert.Single(first.Data).PackageId);
            #pragma warning restore CS8604
        }

        [Fact]
        public async Task RelevanceKeepsTheExistingIdOrder()
        {
            await AddPackageAsync("B", "1.0.0", downloads: 999);
            await AddPackageAsync("A", "1.0.0", downloads: 1);

            var response = await _target.SearchAsync(Sorted(SearchSort.Relevance), _cancellationToken);

            #pragma warning disable CS8604
            Assert.Equal(new[] { "A", "B" }, response.Data.Select(r => r.PackageId));
            #pragma warning restore CS8604
        }
    }

    public class SearchAsync : DatabaseSearchServiceTests
    {
        [Fact]
        public async Task TotalHitsCountsEveryMatchWhenThereIsNoPaging()
        {
            await AddPackagesAsync("Package.A", "Package.B", "Package.C");

            var response = await _target.SearchAsync(Request(skip: 0, take: 20), _cancellationToken);

            #pragma warning disable CS8602
            Assert.Equal(3, response.Data.Count);
            #pragma warning restore CS8602
            Assert.Equal(3, response.TotalHits);
        }

        [Fact]
        public async Task TotalHitsDisregardsSkipAndTake()
        {
            await AddPackagesAsync(PackageIds(10));

            var firstPage = await _target.SearchAsync(Request(skip: 0, take: 3), _cancellationToken);
            var secondPage = await _target.SearchAsync(Request(skip: 3, take: 3), _cancellationToken);
            var lastPage = await _target.SearchAsync(Request(skip: 9, take: 3), _cancellationToken);

            #pragma warning disable CS8602
            Assert.Equal(3, firstPage.Data.Count);
            Assert.Equal(3, secondPage.Data.Count);
            #pragma warning restore CS8602
            #pragma warning disable CS8604
            Assert.Single(lastPage.Data);
            #pragma warning restore CS8604

            // The total is the number of matches, not the size of the page returned.
            Assert.Equal(10, firstPage.TotalHits);
            Assert.Equal(10, secondPage.TotalHits);
            Assert.Equal(10, lastPage.TotalHits);
        }

        [Fact]
        public async Task TotalHitsIsStillTheTotalWhenSkippingPastTheEnd()
        {
            await AddPackagesAsync(PackageIds(5));

            var response = await _target.SearchAsync(Request(skip: 100, take: 20), _cancellationToken);

            #pragma warning disable CS8604
            Assert.Empty(response.Data);
            #pragma warning restore CS8604
            Assert.Equal(5, response.TotalHits);
        }

        [Fact]
        public async Task TotalHitsIsZeroWhenNothingMatches()
        {
            await AddPackagesAsync("Package.A", "Package.B");

            var response = await _target.SearchAsync(
                Request(skip: 0, take: 20, query: "no.such.package"),
                _cancellationToken);

            #pragma warning disable CS8604
            Assert.Empty(response.Data);
            #pragma warning restore CS8604
            Assert.Equal(0, response.TotalHits);
        }

        [Fact]
        public async Task TotalHitsIsZeroWhenTheDatabaseIsEmpty()
        {
            var response = await _target.SearchAsync(Request(skip: 0, take: 20), _cancellationToken);

            #pragma warning disable CS8604
            Assert.Empty(response.Data);
            #pragma warning restore CS8604
            Assert.Equal(0, response.TotalHits);
        }

        [Fact]
        public async Task TotalHitsCountsDistinctPackageIdsAndNotRows()
        {
            // Three rows for one package and one row for another: two matches, not four.
            await AddPackageAsync("Package.A", "1.0.0");
            await AddPackageAsync("Package.A", "2.0.0");
            await AddPackageAsync("Package.A", "3.0.0");
            await AddPackageAsync("Package.B", "1.0.0");

            var response = await _target.SearchAsync(Request(skip: 0, take: 20), _cancellationToken);

            #pragma warning disable CS8602
            Assert.Equal(2, response.Data.Count);
            #pragma warning restore CS8602
            Assert.Equal(2, response.TotalHits);
        }

        [Fact]
        public async Task TotalHitsAppliesTheSearchFilters()
        {
            await AddPackageAsync("Package.Listed", "1.0.0");
            await AddPackageAsync("Package.Unlisted", "1.0.0", listed: false);
            await AddPackageAsync("Package.Prerelease", "1.0.0-beta");

            var excluded = await _target.SearchAsync(
                Request(skip: 0, take: 1, includePrerelease: false),
                _cancellationToken);
            var included = await _target.SearchAsync(
                Request(skip: 0, take: 1, includePrerelease: true),
                _cancellationToken);

            // The unlisted package never matches; the prerelease one matches only when asked for.
            Assert.Equal(1, excluded.TotalHits);
            Assert.Equal(2, included.TotalHits);
        }
    }

    public class AutocompleteAsync : DatabaseSearchServiceTests
    {
        [Fact]
        public async Task TotalHitsCountsEveryMatchWhenThereIsNoPaging()
        {
            await AddPackagesAsync("Package.A", "Package.B", "Package.C");

            var response = await _target.AutocompleteAsync(
                new AutocompleteRequest { Skip = 0, Take = 20 },
                _cancellationToken);

            #pragma warning disable CS8602
            Assert.Equal(3, response.Data.Count);
            #pragma warning restore CS8602
            Assert.Equal(3, response.TotalHits);
        }

        [Fact]
        public async Task TotalHitsDisregardsSkipAndTake()
        {
            await AddPackagesAsync(PackageIds(10));

            var response = await _target.AutocompleteAsync(
                new AutocompleteRequest { Skip = 2, Take = 3 },
                _cancellationToken);

            #pragma warning disable CS8602
            Assert.Equal(3, response.Data.Count);
            #pragma warning restore CS8602
            Assert.Equal(10, response.TotalHits);
        }

        [Fact]
        public async Task TotalHitsIsStillTheTotalWhenSkippingPastTheEnd()
        {
            await AddPackagesAsync(PackageIds(5));

            var response = await _target.AutocompleteAsync(
                new AutocompleteRequest { Skip = 100, Take = 20 },
                _cancellationToken);

            #pragma warning disable CS8604
            Assert.Empty(response.Data);
            #pragma warning restore CS8604
            Assert.Equal(5, response.TotalHits);
        }

        [Fact]
        public async Task TotalHitsIsZeroWhenNothingMatches()
        {
            await AddPackagesAsync("Package.A", "Package.B");

            var response = await _target.AutocompleteAsync(
                new AutocompleteRequest { Skip = 0, Take = 20, Query = "no.such.package" },
                _cancellationToken);

            #pragma warning disable CS8604
            Assert.Empty(response.Data);
            #pragma warning restore CS8604
            Assert.Equal(0, response.TotalHits);
        }

        [Fact]
        public async Task TotalHitsCountsDistinctPackageIdsAndNotRows()
        {
            await AddPackageAsync("Package.A", "1.0.0");
            await AddPackageAsync("Package.A", "2.0.0");
            await AddPackageAsync("Package.B", "1.0.0");

            var response = await _target.AutocompleteAsync(
                new AutocompleteRequest { Skip = 0, Take = 20 },
                _cancellationToken);

            #pragma warning disable CS8602
            Assert.Equal(2, response.Data.Count);
            #pragma warning restore CS8602
            Assert.Equal(2, response.TotalHits);
        }

        [Fact]
        public async Task OrdersPackageIdsByTotalDownloads()
        {
            // Guards the fix for #771. Ordering before a "Distinct" over a narrower
            // projection is dropped, which left the ids in insertion order.
            await AddPackageAsync("Package.Unpopular", "1.0.0", downloads: 1);
            await AddPackageAsync("Package.Unpopular", "2.0.0", downloads: 2);
            await AddPackageAsync("Package.Popular", "1.0.0", downloads: 500);
            await AddPackageAsync("Package.Popular", "2.0.0", downloads: 500);
            await AddPackageAsync("Package.Middling", "1.0.0", downloads: 50);

            var response = await _target.AutocompleteAsync(
                new AutocompleteRequest { Skip = 0, Take = 20 },
                _cancellationToken);

            Assert.Equal(
                new[] { "Package.Popular", "Package.Middling", "Package.Unpopular" },
                response.Data);
        }

        [Fact]
        public async Task ReturnsEachPackageIdOnce()
        {
            await AddPackageAsync("Package.A", "1.0.0", downloads: 5);
            await AddPackageAsync("Package.A", "2.0.0", downloads: 5);
            await AddPackageAsync("Package.A", "3.0.0", downloads: 5);

            var response = await _target.AutocompleteAsync(
                new AutocompleteRequest { Skip = 0, Take = 20 },
                _cancellationToken);

            Assert.Equal(new[] { "Package.A" }, response.Data);
        }

        [Fact]
        public async Task PagesOverDistinctPackageIds()
        {
            await AddPackageAsync("Package.First", "1.0.0", downloads: 300);
            await AddPackageAsync("Package.First", "2.0.0", downloads: 300);
            await AddPackageAsync("Package.Second", "1.0.0", downloads: 200);
            await AddPackageAsync("Package.Third", "1.0.0", downloads: 100);

            var response = await _target.AutocompleteAsync(
                new AutocompleteRequest { Skip = 1, Take = 1 },
                _cancellationToken);

            Assert.Equal(new[] { "Package.Second" }, response.Data);
        }
    }

    public class ListPackageVersionsAsync : DatabaseSearchServiceTests
    {
        [Fact]
        public async Task TotalHitsCountsTheVersionsReturned()
        {
            await AddPackageAsync("Package.A", "1.0.0");
            await AddPackageAsync("Package.A", "2.0.0");
            await AddPackageAsync("Package.B", "1.0.0");

            var response = await _target.ListPackageVersionsAsync(
                new VersionsRequest { PackageId = "Package.A" },
                _cancellationToken);

            #pragma warning disable CS8602
            Assert.Equal(2, response.Data.Count);
            #pragma warning restore CS8602
            Assert.Equal(2, response.TotalHits);
        }

        [Fact]
        public async Task TotalHitsIsZeroWhenThePackageDoesNotExist()
        {
            await AddPackageAsync("Package.A", "1.0.0");

            var response = await _target.ListPackageVersionsAsync(
                new VersionsRequest { PackageId = "No.Such.Package" },
                _cancellationToken);

            #pragma warning disable CS8604
            Assert.Empty(response.Data);
            #pragma warning restore CS8604
            Assert.Equal(0, response.TotalHits);
        }
    }

    private static SearchRequest Request(
        int skip,
        int take,
        string? query = null,
        bool includePrerelease = true,
        bool includeSemVer2 = true)
    {
        return new SearchRequest
        {
            Skip = skip,
            Take = take,
            Query = query,
            IncludePrerelease = includePrerelease,
            IncludeSemVer2 = includeSemVer2,
        };
    }

    private static string[] PackageIds(int count)
    {
        return Enumerable
            .Range(0, count)
            .Select(i => $"Package.{i:00}")
            .ToArray();
    }

    private async Task AddPackagesAsync(params string[] packageIds)
    {
        foreach (var packageId in packageIds)
        {
            await AddPackageAsync(packageId, "1.0.0");
        }
    }

    private async Task AddPackageAsync(
        string packageId,
        string version,
        bool listed = true,
        long downloads = 0,
        DateTime? published = null)
    {
        var parsedVersion = NuGetVersion.Parse(version);

        #pragma warning disable CS8602
        _context.Packages.Add(new Package
        {
            Id = packageId,
            Version = parsedVersion,
            Authors = Array.Empty<string>(),
            Description = "Description",
            Listed = listed,
            Downloads = downloads,
            IsPrerelease = parsedVersion.IsPrerelease,
            SemVerLevel = SemVerLevel.Unknown,
            Published = published ?? DateTime.UtcNow,
            Summary = "Summary",
            Tags = Array.Empty<string>(),
            Dependencies = new List<PackageDependency>(),
            PackageTypes = new List<PackageType>(),
            TargetFrameworks = new List<TargetFramework>(),
        });
        #pragma warning restore CS8602

        await _context.SaveChangesAsync(_cancellationToken);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}