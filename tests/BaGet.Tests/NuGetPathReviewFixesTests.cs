using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Catalog.Database;
using BaGet.Core;
using BaGet.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>Regression tests of the NuGet-path code review (2026-10-28), against a real SQLite database.</summary>
public class NuGetPathReviewFixesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteContext _context;
    private readonly CancellationToken _ct = CancellationToken.None;

    public NuGetPathReviewFixesTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _context = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(_connection).Options);
        _context.Database.Migrate();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task DependentsListsEachIdOnceWithItsDownloadsSummed()
    {
        // 25 versions of one popular dependent used to fill the 20 rows before the distinct step.
        for (var i = 1; i <= 25; i++)
        {
            Add("Popular", $"{i}.0.0", downloads: i, dependsOn: "Lib");
        }

        Add("Other", "1.0.0", downloads: 1, dependsOn: "Lib");
        Add("Unrelated", "1.0.0", downloads: 1000, dependsOn: "Else");
        await _context.SaveChangesAsync(_ct);

        var search = new DatabaseSearchService(
            _context,
            new FrameworkCompatibilityService(),
            new SearchResponseBuilder(Mock.Of<IUrlGenerator>()));

        var response = await search.FindDependentsAsync("Lib", _ct);

        #pragma warning disable CS8602
        Assert.Equal(new[] { "Popular", "Other" }, response.Data!.Select(d => d.Id));
        Assert.Equal(325, response.Data[0].TotalDownloads);
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task FindManyReturnsTheVersionsOfEveryIdInOneCall()
    {
        Add("A", "1.0.0");
        Add("A", "2.0.0");
        Add("B", "1.0.0");
        Add("C", "1.0.0");
        Add("B", "2.0.0", listed: false);
        await _context.SaveChangesAsync(_ct);

        IPackageDatabase db = new PackageDatabase(_context);

        var listed = await db.FindManyAsync(new[] { "A", "B" }, includeUnlisted: false, _ct);
        Assert.Equal(new[] { "A 1.0.0", "A 2.0.0", "B 1.0.0" }, listed.Select(p => $"{p.Id} {p.NormalizedVersionString}").OrderBy(s => s));

        var all = await db.FindManyAsync(new[] { "B" }, includeUnlisted: true, _ct);
        Assert.Equal(2, all.Count);
        Assert.Empty(await db.FindManyAsync(Array.Empty<string>(), includeUnlisted: true, _ct));
    }

    [Fact]
    public async Task UnlistingAndCountingStillWork()
    {
        Add("A", "1.0.0");
        await _context.SaveChangesAsync(_ct);
        var db = new PackageDatabase(_context);

        await db.AddDownloadAsync("A", NuGetVersion.Parse("1.0.0"), _ct);
        Assert.True(await db.UnlistPackageAsync("A", NuGetVersion.Parse("1.0.0"), _ct));
        Assert.False(await db.UnlistPackageAsync("Missing", NuGetVersion.Parse("1.0.0"), _ct));

        var package = await db.FindOrNullAsync("A", NuGetVersion.Parse("1.0.0"), includeUnlisted: true, _ct);
        Assert.Equal(1, package!.Downloads);
        Assert.False(package.Listed);
    }

    [Fact]
    public async Task TheCatalogIndexNeedsOnlyThePageEnds()
    {
        for (var i = 0; i < 5; i++)
        {
            _context.CatalogLeafs!.Add(new CatalogEntry
            {
                CommitId = Guid.NewGuid().ToString("D"),
                CommitTimestamp = new DateTime(2020, 1, 1, 0, 0, i, DateTimeKind.Utc),
                Type = "PackageDetails",
                PackageId = "A",
                PackageVersion = "1.0.0",
            });
        }

        await _context.SaveChangesAsync(_ct);
        ICatalogDatabase catalog = new CatalogDatabase(_context);

        var (count, ends) = await catalog.GetPageEndsAsync(2, _ct);

        Assert.Equal(5, count);
        Assert.Equal(new[] { 1, 3, 4 }, ends.Select(e => e.Second));

        var (none, noEnds) = await new CatalogDatabase(_context).GetPageEndsAsync(500, _ct);
        Assert.Equal(5, none);
        Assert.Single(noEnds);
        Assert.Equal(4, noEnds[0].Second);
    }

    [Theory]
    [InlineData(int.MaxValue, 1000)]
    [InlineData(-5, 0)]
    [InlineData(20, 20)]
    public void SearchTakeIsClamped(int take, int expected)
        => Assert.Equal(expected, BaGet.Web.V3.Controllers.Protocols.SearchController.ClampTake(take));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(7, 7)]
    public void SearchSkipIsNotNegative(int skip, int expected)
        => Assert.Equal(expected, BaGet.Web.V3.Controllers.Protocols.SearchController.ClampSkip(skip));

    private void Add(string id, string version, long downloads = 0, bool listed = true, string? dependsOn = null)
    {
        var parsed = NuGetVersion.Parse(version);
        #pragma warning disable CS8602
        _context.Packages.Add(new Package
        {
            Id = id,
            Version = parsed,
            Authors = Array.Empty<string>(),
            Description = "Description",
            Listed = listed,
            Downloads = downloads,
            IsPrerelease = parsed.IsPrerelease,
            SemVerLevel = SemVerLevel.Unknown,
            Published = DateTime.UtcNow,
            Summary = "Summary",
            Tags = Array.Empty<string>(),
            Dependencies = dependsOn == null
                ? new List<PackageDependency>()
                : new List<PackageDependency> { new() { Id = dependsOn, VersionRange = "[1.0.0,)", TargetFramework = "net8.0" } },
            PackageTypes = new List<PackageType>(),
            TargetFrameworks = new List<TargetFramework>(),
        });
        #pragma warning restore CS8602
    }
}
