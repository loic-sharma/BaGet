using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Database.Sqlite;
using BaGet.Protocol.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests
{
    public class DatabaseSearchServiceTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly SqliteContext _context;
        private readonly DatabaseSearchService _target;

        private IReadOnlyList<string> _autocompleted;

        public DatabaseSearchServiceTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = new DbContextOptionsBuilder<SqliteContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new SqliteContext(options);
            _context.Database.EnsureCreated();

            var searchBuilder = new Mock<ISearchResponseBuilder>();
            searchBuilder
                .Setup(b => b.BuildAutocomplete(It.IsAny<IReadOnlyList<string>>()))
                .Callback<IReadOnlyList<string>>(data => _autocompleted = data)
                .Returns(new AutocompleteResponse());

            _target = new DatabaseSearchService(
                _context,
                Mock.Of<IFrameworkCompatibilityService>(),
                searchBuilder.Object);
        }

        [Fact]
        public async Task AutocompleteOrdersPackageIdsByTotalDownloads()
        {
            // Each package has several versions so that the query must aggregate
            // downloads per package ID rather than per version.
            AddPackage("Package.Unpopular", "1.0.0", downloads: 1);
            AddPackage("Package.Unpopular", "2.0.0", downloads: 2);
            AddPackage("Package.Popular", "1.0.0", downloads: 500);
            AddPackage("Package.Popular", "2.0.0", downloads: 500);
            AddPackage("Package.Middling", "1.0.0", downloads: 50);
            await _context.SaveChangesAsync();

            await _target.AutocompleteAsync(
                new AutocompleteRequest { Skip = 0, Take = 20, IncludePrerelease = true, IncludeSemVer2 = true },
                CancellationToken.None);

            Assert.Equal(
                new[] { "Package.Popular", "Package.Middling", "Package.Unpopular" },
                _autocompleted);
        }

        [Fact]
        public async Task AutocompleteReturnsEachPackageIdOnce()
        {
            AddPackage("Package.A", "1.0.0", downloads: 5);
            AddPackage("Package.A", "2.0.0", downloads: 5);
            AddPackage("Package.A", "3.0.0", downloads: 5);
            await _context.SaveChangesAsync();

            await _target.AutocompleteAsync(
                new AutocompleteRequest { Skip = 0, Take = 20, IncludePrerelease = true, IncludeSemVer2 = true },
                CancellationToken.None);

            Assert.Equal(new[] { "Package.A" }, _autocompleted);
        }

        [Fact]
        public async Task AutocompletePagesOverDistinctPackageIds()
        {
            AddPackage("Package.First", "1.0.0", downloads: 300);
            AddPackage("Package.First", "2.0.0", downloads: 300);
            AddPackage("Package.Second", "1.0.0", downloads: 200);
            AddPackage("Package.Third", "1.0.0", downloads: 100);
            await _context.SaveChangesAsync();

            await _target.AutocompleteAsync(
                new AutocompleteRequest { Skip = 1, Take = 1, IncludePrerelease = true, IncludeSemVer2 = true },
                CancellationToken.None);

            Assert.Equal(new[] { "Package.Second" }, _autocompleted);
        }

        private void AddPackage(string id, string version, long downloads)
        {
            _context.Packages.Add(new Package
            {
                Id = id,
                Version = NuGetVersion.Parse(version),
                Downloads = downloads,
                Listed = true,
                IsPrerelease = false,
                SemVerLevel = SemVerLevel.Unknown,
                Authors = Array.Empty<string>(),
                Tags = Array.Empty<string>(),
                Description = "",
                Published = DateTime.UtcNow,
                Dependencies = new List<PackageDependency>(),
                PackageTypes = new List<PackageType>(),
                TargetFrameworks = new List<TargetFramework>(),
            });
        }

        public void Dispose()
        {
            _context?.Dispose();
            _connection?.Dispose();
        }
    }
}
