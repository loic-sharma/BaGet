using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Services;

public class PackageDatabaseTests
{
    public class AddAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsPackageAlreadyExistsOnUniqueConstraintViolation()
        {
            // "Package" 1.0.0 is already in the database.
            var result = await _target.AddAsync(CreatePackage("Package", "1.0.0"), Cancellation);

            Assert.Equal(PackageAddResult.PackageAlreadyExists, result);
        }

        [Fact]
        public async Task AddsPackage()
        {
            var result = await _target.AddAsync(CreatePackage("New.Package", "2.0.0"), Cancellation);

            Assert.Equal(PackageAddResult.Success, result);
            Assert.True(await _target.ExistsAsync("New.Package", NuGetVersion.Parse("2.0.0"), Cancellation));
        }

        [Fact]
        public async Task AnotherVersionOfAnExistingPackageIsNotAConflict()
        {
            var result = await _target.AddAsync(CreatePackage("Package", "2.0.0"), Cancellation);

            Assert.Equal(PackageAddResult.Success, result);
        }

        [Fact]
        public async Task StoresTheNormalizedVersion()
        {
            await _target.AddAsync(CreatePackage("New.Package", "1.2.3.0"), Cancellation);

            var package = await _target.FindOrNullAsync(
                "New.Package", NuGetVersion.Parse("1.2.3"), includeUnlisted: true, Cancellation);

            Assert.NotNull(package);
            Assert.Equal("1.2.3", package.NormalizedVersionString);
        }
    }

    public class ExistsAsync : FactsBase
    {
        [Theory]
        [InlineData("Package", "1.0.0", true)]
        [InlineData("Package", "1.0.0.0", true)]
        [InlineData("Unlisted.Package", "1.0.0", true)]
        [InlineData("Fake.Package", "1.0.0", false)]
        public async Task ReturnsTrueIfPackageExists(string? packageId, string packageVersion, bool exists)
        {
            var result = await _target.ExistsAsync(
                packageId, NuGetVersion.Parse(packageVersion), Cancellation);

            Assert.Equal(exists, result);
        }

        [Theory]
        [InlineData("Package", true)]
        [InlineData("Unlisted.Package", true)]
        [InlineData("Fake.Package", false)]
        public async Task TheIdOnlyOverloadIgnoresTheVersion(string? packageId, bool exists)
        {
            Assert.Equal(exists, await _target.ExistsAsync(packageId, Cancellation));
        }

        [Fact]
        public async Task IsFalseForAVersionThatDoesNotExist()
        {
            Assert.False(await _target.ExistsAsync(
                "Package", NuGetVersion.Parse("9.9.9"), Cancellation));
        }
    }

    public class FindAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsEmptyListIfPackageDoesNotExist()
        {
            var result = await _target.FindAsync("Fake.Package", includeUnlisted: true, Cancellation);

            Assert.Empty(result);
        }

        [Theory]
        [MemberData(nameof(ReturnsPackagesData))]
        public async Task ReturnsPackages(string? packageId, string packageVersion, bool includeUnlisted, bool exists)
        {
            var result = await _target.FindAsync(packageId, includeUnlisted, Cancellation);

            var version = NuGetVersion.Parse(packageVersion).ToNormalizedString();
            Assert.Equal(
                exists,
                result.Any(p => p.NormalizedVersionString == version));
        }

        [Fact]
        public async Task ReturnsEveryVersionOfThePackage()
        {
            await _target.AddAsync(CreatePackage("Package", "2.0.0"), Cancellation);

            var result = await _target.FindAsync("Package", includeUnlisted: true, Cancellation);

            Assert.Equal(
                new[] { "1.0.0", "2.0.0" },
                result.Select(p => p.NormalizedVersionString).OrderBy(v => v));
        }

        [Fact]
        public async Task LoadsTheRelatedCollections()
        {
            var result = await _target.FindAsync("Package", includeUnlisted: true, Cancellation);

            var package = Assert.Single(result);
            #pragma warning disable CS8604
            Assert.Equal("Other.Package", Assert.Single(package.Dependencies).Id);
            Assert.Equal("netstandard2.0", Assert.Single(package.TargetFrameworks).Moniker);
            Assert.Equal("Dependency", Assert.Single(package.PackageTypes).Name);
            #pragma warning restore CS8604
        }

        public static IEnumerable<object[]> ReturnsPackagesData()
        {
            object[] ReturnsPackagesHelper(string packageId, string packageVersion, bool includeUnlisted, bool exists)
            {
                return new object[] { packageId, packageVersion, includeUnlisted, exists };
            }

            // A package that doesn't exist should never be returned
            yield return ReturnsPackagesHelper("Fake.Package", "1.0.0", includeUnlisted: true, exists: false);

            // A listed package should be returned regardless of the "includeUnlisted" parameter
            yield return ReturnsPackagesHelper("Package", "1.0.0", includeUnlisted: false, exists: true);
            yield return ReturnsPackagesHelper("Package", "1.0.0", includeUnlisted: true, exists: true);

            // The inputted package version should be normalized
            yield return ReturnsPackagesHelper("Package", "1.0.0.0", includeUnlisted: false, exists: true);

            // Unlisted packages should only be returned if "includeUnlisted" is true
            yield return ReturnsPackagesHelper("Unlisted.Package", "1.0.0", includeUnlisted: false, exists: false);
            yield return ReturnsPackagesHelper("Unlisted.Package", "1.0.0", includeUnlisted: true, exists: true);
        }
    }

    public class FindOrNullAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullIfPackageDoesNotExist()
        {
            var result = await _target.FindOrNullAsync(
                "Fake.Package", NuGetVersion.Parse("1.0.0"), includeUnlisted: true, Cancellation);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsNullIfTheVersionDoesNotExist()
        {
            var result = await _target.FindOrNullAsync(
                "Package", NuGetVersion.Parse("9.9.9"), includeUnlisted: true, Cancellation);

            Assert.Null(result);
        }

        [Theory]
        [MemberData(nameof(ReturnsPackageData))]
        public async Task ReturnsPackage(string packageId, string packageVersion, bool includeUnlisted, bool exists)
        {
            var result = await _target.FindOrNullAsync(
                packageId, NuGetVersion.Parse(packageVersion), includeUnlisted, Cancellation);

            if (!exists)
            {
                Assert.Null(result);
                return;
            }

            Assert.NotNull(result);
            Assert.Equal(packageId, result.Id);
            Assert.Equal(
                NuGetVersion.Parse(packageVersion).ToNormalizedString(),
                result.NormalizedVersionString);
        }

        [Fact]
        public async Task LoadsTheDependenciesAndFrameworks()
        {
            var result = await _target.FindOrNullAsync(
                "Package", NuGetVersion.Parse("1.0.0"), includeUnlisted: true, Cancellation);

            #pragma warning disable CS8604
            Assert.Equal("Other.Package", Assert.Single(result?.Dependencies).Id);
            Assert.Equal("netstandard2.0", Assert.Single(result?.TargetFrameworks).Moniker);
            #pragma warning restore CS8604
        }

        public static IEnumerable<object[]> ReturnsPackageData()
        {
            object[] ReturnsPackageHelper(string packageId, string packageVersion, bool includeUnlisted, bool exists)
            {
                return new object[] { packageId, packageVersion, includeUnlisted, exists };
            }

            // A package that doesn't exist should never be returned
            yield return ReturnsPackageHelper("Fake.Package", "1.0.0", includeUnlisted: true, exists: false);

            // A listed package should be returned regardless of the "includeUnlisted" parameter
            yield return ReturnsPackageHelper("Package", "1.0.0", includeUnlisted: false, exists: true);
            yield return ReturnsPackageHelper("Package", "1.0.0", includeUnlisted: true, exists: true);

            // The inputted package version should be normalized
            yield return ReturnsPackageHelper("Package", "1.0.0.0", includeUnlisted: false, exists: true);

            // Unlisted packages should only be returned if "includeUnlisted" is true
            yield return ReturnsPackageHelper("Unlisted.Package", "1.0.0", includeUnlisted: false, exists: false);
            yield return ReturnsPackageHelper("Unlisted.Package", "1.0.0", includeUnlisted: true, exists: true);
        }
    }

    public class UnlistPackageAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFalseIfPackageDoesNotExist()
        {
            Assert.False(await _target.UnlistPackageAsync(
                "Fake.Package", NuGetVersion.Parse("1.0.0"), Cancellation));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task UnlistsPackage(bool listed)
        {
            // Unlisting is idempotent: it succeeds whatever the current state.
            var id = listed ? "Package" : "Unlisted.Package";

            var result = await _target.UnlistPackageAsync(id, NuGetVersion.Parse("1.0.0"), Cancellation);

            Assert.True(result);
            Assert.False(await IsListed(id, "1.0.0"));
        }
    }

    public class RelistPackageAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFalseIfPackageDoesNotExist()
        {
            Assert.False(await _target.RelistPackageAsync(
                "Fake.Package", NuGetVersion.Parse("1.0.0"), Cancellation));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RelistsPackage(bool listed)
        {
            // Relisting is idempotent: it succeeds whatever the current state.
            var id = listed ? "Package" : "Unlisted.Package";

            var result = await _target.RelistPackageAsync(id, NuGetVersion.Parse("1.0.0"), Cancellation);

            Assert.True(result);
            Assert.True(await IsListed(id, "1.0.0"));
        }
    }

    public class AddDownloadAsync : FactsBase
    {
        [Fact]
        public async Task DoesNothingIfPackageDoesNotExist()
        {
            await _target.AddDownloadAsync("Fake.Package", NuGetVersion.Parse("1.0.0"), Cancellation);

            Assert.False(await _target.ExistsAsync("Fake.Package", Cancellation));
        }

        [Fact]
        public async Task IncrementsPackageDownloads()
        {
            await _target.AddDownloadAsync("Package", NuGetVersion.Parse("1.0.0"), Cancellation);

            Assert.Equal(1, await Downloads("Package", "1.0.0"));
        }

        [Fact]
        public async Task IncrementsOncePerCall()
        {
            await _target.AddDownloadAsync("Package", NuGetVersion.Parse("1.0.0"), Cancellation);
            await _target.AddDownloadAsync("Package", NuGetVersion.Parse("1.0.0"), Cancellation);
            await _target.AddDownloadAsync("Package", NuGetVersion.Parse("1.0.0"), Cancellation);

            Assert.Equal(3, await Downloads("Package", "1.0.0"));
        }

        [Fact]
        public async Task CountsAnUnlistedPackagesDownloads()
        {
            await _target.AddDownloadAsync("Unlisted.Package", NuGetVersion.Parse("1.0.0"), Cancellation);

            Assert.Equal(1, await Downloads("Unlisted.Package", "1.0.0"));
        }
    }

    public class HardDeletePackageAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFalseIfPackageDoesNotExist()
        {
            Assert.False(await _target.HardDeletePackageAsync(
                "Fake.Package", NuGetVersion.Parse("1.0.0"), Cancellation));
        }

        [Fact]
        public async Task DeletesPackage()
        {
            var result = await _target.HardDeletePackageAsync(
                "Package", NuGetVersion.Parse("1.0.0"), Cancellation);

            Assert.True(result);
            Assert.False(await _target.ExistsAsync("Package", NuGetVersion.Parse("1.0.0"), Cancellation));
        }

        [Fact]
        public async Task DeletesTheDependentRows()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // The rows belong to the package: they must go with it, rather than being
            // left behind with a null package reference.
            await _target.HardDeletePackageAsync("Package", NuGetVersion.Parse("1.0.0"), Cancellation);

            #pragma warning disable CS8604
            var dependencies = await _context.PackageDependencies
                .AsNoTracking()
                .Include(d => d.Package)
                .ToListAsync(cancellationToken);
            var frameworks = await _context.TargetFrameworks
                .AsNoTracking()
                .Include(f => f.Package)
                .ToListAsync(cancellationToken);
            #pragma warning restore CS8604

            Assert.DoesNotContain(dependencies, d => d.Package == null || d.Package.Id == "Package");
            Assert.DoesNotContain(frameworks, f => f.Package == null || f.Package.Id == "Package");

            // The other package keeps its own rows.
            Assert.Single(dependencies);
            Assert.Single(frameworks);
        }

        [Fact]
        public async Task LeavesOtherPackagesAlone()
        {
            await _target.HardDeletePackageAsync("Package", NuGetVersion.Parse("1.0.0"), Cancellation);

            Assert.True(await _target.ExistsAsync(
                "Unlisted.Package", NuGetVersion.Parse("1.0.0"), Cancellation));
        }

        [Fact]
        public async Task DeletesOnlyTheRequestedVersion()
        {
            await _target.AddAsync(CreatePackage("Package", "2.0.0"), Cancellation);

            await _target.HardDeletePackageAsync("Package", NuGetVersion.Parse("1.0.0"), Cancellation);

            Assert.False(await _target.ExistsAsync("Package", NuGetVersion.Parse("1.0.0"), Cancellation));
            Assert.True(await _target.ExistsAsync("Package", NuGetVersion.Parse("2.0.0"), Cancellation));
        }
    }

    public class FactsBase : IDisposable
    {
        protected static readonly CancellationToken Cancellation = CancellationToken.None;

        private readonly SqliteConnection _connection;
        protected readonly SqliteContext _context;
        protected readonly PackageDatabase _target;

        public FactsBase()
        {
            // A real database, so the queries themselves are covered.
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _context = new SqliteContext(
                new DbContextOptionsBuilder<SqliteContext>()
                    .UseSqlite(_connection)
                    .Options);
            _context.Database.EnsureCreated();

            // EnsureCreated runs no migration, so the default repository the packages belong to is added here.
            _context.Repositories?.Add(new Repository { Key = Repository.DefaultKey, Name = Repository.DefaultName, Created = DateTime.UtcNow });
            _context.SaveChanges();

            _context.Packages?.Add(CreatePackage("Package", "1.0.0"));
            _context.Packages?.Add(CreatePackage("Unlisted.Package", "1.0.0", listed: false));
            _context.SaveChanges();

            _target = new PackageDatabase(_context);
        }

        protected static Package CreatePackage(string id, string version, bool listed = true)
            => new()
            {
                Id = id,
                Version = NuGetVersion.Parse(version),
                Listed = listed,
                Downloads = 0,
                Authors = new[] { "author" },
                Tags = new string[0],
                Description = "A test package.",
                Language = string.Empty,
                MinClientVersion = string.Empty,
                ReleaseNotes = string.Empty,
                Published = DateTime.UtcNow,
                SemVerLevel = SemVerLevel.Unknown,
                Dependencies = new List<PackageDependency>
                {
                    new()
                    {
                        Id = "Other.Package",
                        VersionRange = "[1.0.0, )",
                        TargetFramework = "netstandard2.0",
                    },
                },
                PackageTypes = new List<PackageType>
                {
                    new() { Name = "Dependency", Version = "0.0" },
                },
                TargetFrameworks = new List<TargetFramework>
                {
                    new() { Moniker = "netstandard2.0" },
                },
            };

        protected async Task<bool> IsListed(string id, string version)
        {
            var package = await Reread(id, version);
            return package.Listed;
        }

        protected async Task<long> Downloads(string id, string version)
        {
            var package = await Reread(id, version);
            return package.Downloads;
        }

        private async Task<Package> Reread(string id, string version)
        {
            var normalized = NuGetVersion.Parse(version).ToNormalizedString();

            #pragma warning disable CS8604
            return await _context.Packages
                .AsNoTracking()
                .FirstAsync(p => p.Id == id && p.NormalizedVersionString == normalized);
            #pragma warning restore CS8604
        }

        public void Dispose()
        {
            _context?.Dispose();
            _connection?.Dispose();
        }
    }
}