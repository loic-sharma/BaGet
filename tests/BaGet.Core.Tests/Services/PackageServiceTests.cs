using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests
{
    public class PackageServiceTests
    {
        public class FindPackageVersionsAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsEmpty()
            {
                Setup();

                var results = await _target.FindPackageVersionsAsync(
                    "MyPackage",
                    _cancellationToken);

                Assert.Empty(results);
            }

            [Fact]
            public async Task ReturnsLocalVersions()
            {
                Setup(localPackages: new List<Package>
                {
                    new Package { Version = new NuGetVersion("1.0.0") },
                    new Package { Version = new NuGetVersion("2.0.0") },
                });

                var results = await _target.FindPackageVersionsAsync(
                    "MyPackage",
                    _cancellationToken);

                Assert.Equal(2, results.Count);
                Assert.Equal("1.0.0", results[0].OriginalVersion);
                Assert.Equal("2.0.0", results[1].OriginalVersion);
            }

            [Fact]
            public async Task ReturnsUpstreamVersions()
            {
                Setup(upstreamPackages: new List<NuGetVersion>
                {
                    new NuGetVersion("1.0.0"),
                    new NuGetVersion("2.0.0"),
                });

                var results = await _target.FindPackageVersionsAsync(
                    "MyPackage",
                    _cancellationToken);

                Assert.Equal(2, results.Count);
                Assert.Equal("1.0.0", results[0].OriginalVersion);
                Assert.Equal("2.0.0", results[1].OriginalVersion);
            }

            [Fact]
            public async Task MergesLocalAndUpstreamVersions()
            {
                Setup(
                    localPackages: new List<Package>
                    {
                        new Package { Version = new NuGetVersion("1.0.0") },
                        new Package { Version = new NuGetVersion("2.0.0") },
                    },
                    upstreamPackages: new List<NuGetVersion>
                    {
                        new NuGetVersion("2.0.0"),
                        new NuGetVersion("3.0.0"),
                    });

                var results = await _target.FindPackageVersionsAsync(
                    "MyPackage",
                    _cancellationToken);

                var ordered = results.OrderBy(v => v).ToList();

                Assert.Equal(3, ordered.Count);
                Assert.Equal("1.0.0", ordered[0].OriginalVersion);
                Assert.Equal("2.0.0", ordered[1].OriginalVersion);
                Assert.Equal("3.0.0", ordered[2].OriginalVersion);
            }

            private void Setup(
                IReadOnlyList<Package> localPackages = null,
                IReadOnlyList<NuGetVersion> upstreamPackages = null)
            {
                localPackages = localPackages ?? new List<Package>();
                upstreamPackages = upstreamPackages ?? new List<NuGetVersion>();

                _db
                    .Setup(p => p.FindAsync(
                        "MyPackage",
                        /*includeUnlisted: */ true,
                        _cancellationToken))
                    .ReturnsAsync(localPackages);

                _upstream
                    .Setup(u => u.ListPackageVersionsAsync(
                        "MyPackage",
                        _cancellationToken))
                    .ReturnsAsync(upstreamPackages);
            }
        }

        public class FindPackagesAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsEmpty()
            {
                Setup();

                var results = await _target.FindPackagesAsync("MyPackage", _cancellationToken);

                Assert.Empty(results);
            }

            [Fact]
            public async Task ReturnsLocalPackages()
            {
                Setup(localPackages: new List<Package>
                {
                    new Package { Version = new NuGetVersion("1.0.0") },
                    new Package { Version = new NuGetVersion("2.0.0") },
                });

                var results = await _target.FindPackagesAsync("MyPackage", _cancellationToken);

                Assert.Equal(2, results.Count);
                Assert.Equal("1.0.0", results[0].Version.OriginalVersion);
                Assert.Equal("2.0.0", results[1].Version.OriginalVersion);
            }

            [Fact]
            public async Task ReturnsUpstreamPackages()
            {
                Setup(upstreamPackages: new List<Package>
                {
                    new Package { Version = new NuGetVersion("1.0.0") },
                    new Package { Version = new NuGetVersion("2.0.0") },
                });

                var results = await _target.FindPackagesAsync("MyPackage", _cancellationToken);

                Assert.Equal(2, results.Count);
                Assert.Equal("1.0.0", results[0].Version.OriginalVersion);
                Assert.Equal("2.0.0", results[1].Version.OriginalVersion);
            }

            [Fact]
            public async Task MergesLocalAndUpstreamPackages()
            {
                Setup(
                    localPackages: new List<Package>
                    {
                        new Package { Version = new NuGetVersion("1.0.0") },
                        new Package { Version = new NuGetVersion("2.0.0") },
                    },
                    upstreamPackages: new List<Package>
                    {
                        new Package { Version = new NuGetVersion("2.0.0") },
                        new Package { Version = new NuGetVersion("3.0.0") },
                    });

                var results = await _target.FindPackagesAsync("MyPackage", _cancellationToken);
                var ordered = results.OrderBy(p => p.Version).ToList();

                Assert.Equal(3, ordered.Count);
                Assert.Equal("1.0.0", ordered[0].Version.OriginalVersion);
                Assert.Equal("2.0.0", ordered[1].Version.OriginalVersion);
                Assert.Equal("3.0.0", ordered[2].Version.OriginalVersion);
            }

            private void Setup(
                IReadOnlyList<Package> localPackages = null,
                IReadOnlyList<Package> upstreamPackages = null)
            {
                localPackages = localPackages ?? new List<Package>();
                upstreamPackages = upstreamPackages ?? new List<Package>();

                _db
                    .Setup(p => p.FindAsync(
                        "MyPackage",
                        /*includeUnlisted: */ true,
                        _cancellationToken))
                    .ReturnsAsync(localPackages);

                _upstream
                    .Setup(u => u.ListPackagesAsync(
                        "MyPackage",
                        _cancellationToken))
                    .ReturnsAsync(upstreamPackages);
            }
        }

        public class FindPackageOrNullAsync : MirrorAsync
        {
            protected override async Task TargetAsync()
                => await _target.FindPackageOrNullAsync(_id, _version, _cancellationToken);

            [Fact]
            public async Task ExistsInDatabase()
            {
                var expected = new Package();

                _db
                    .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                    .ReturnsAsync(true);
                _db
                    .Setup(p => p.FindOrNullAsync(_id, _version,  /*includeUnlisted:*/ true, _cancellationToken))
                    .ReturnsAsync(expected);

                var result = await _target.FindPackageOrNullAsync(_id, _version, _cancellationToken);

                Assert.Same(expected, result);
            }

            [Fact]
            public async Task DoesNotExistInDatabase()
            {
                _db
                    .Setup(p => p.FindOrNullAsync(_id, _version,  /*includeUnlisted:*/ true, _cancellationToken))
                    .ReturnsAsync((Package)null);

                var result = await _target.FindPackageOrNullAsync(_id, _version, _cancellationToken);

                Assert.Null(result);
            }
        }

        public class ExistsAsync : MirrorAsync
        {
            protected override async Task TargetAsync() => await _target.ExistsAsync(_id, _version, _cancellationToken);

            [Fact]
            public async Task ExistsInDatabase()
            {
                _db
                    .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                    .ReturnsAsync(true);

                var result = await _target.ExistsAsync(_id, _version, _cancellationToken);

                Assert.True(result);
            }

            [Fact]
            public async Task DoesNotExistInDatabase()
            {
                _db
                    .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                    .ReturnsAsync(false);

                var result = await _target.ExistsAsync(_id, _version, _cancellationToken);

                Assert.False(result);
            }
        }

        public abstract class MirrorAsync : FactsBase
        {
            protected readonly string _id = "MyPackage";
            protected readonly NuGetVersion _version = new NuGetVersion("1.0.0");

            protected abstract Task TargetAsync();

            [Fact]
            public async Task SkipsIfAlreadyMirrored()
            {
                _db
                    .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                    .ReturnsAsync(true);

                await TargetAsync();

                _indexer.Verify(
                    i => i.IndexAsync(It.IsAny<Stream>(), _cancellationToken),
                    Times.Never);
            }

            [Fact]
            public async Task SkipsIfUpstreamDoesntHavePackage()
            {
                _db
                    .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                    .ReturnsAsync(false);

                _upstream
                    .Setup(u => u.DownloadPackageOrNullAsync(_id, _version, _cancellationToken))
                    .ReturnsAsync((Stream)null);

                await TargetAsync();

                _indexer.Verify(
                    i => i.IndexAsync(It.IsAny<Stream>(), _cancellationToken),
                    Times.Never);
            }

            [Fact]
            public async Task SkipsIfUpstreamThrows()
            {
                _db
                    .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                    .ReturnsAsync(false);

                _upstream
                    .Setup(u => u.DownloadPackageOrNullAsync(_id, _version, _cancellationToken))
                    .ThrowsAsync(new InvalidOperationException("Hello world"));

                await TargetAsync();

                _indexer.Verify(
                    i => i.IndexAsync(It.IsAny<Stream>(), _cancellationToken),
                    Times.Never);
            }

            [Fact]
            public async Task MirrorsPackage()
            {
                _db
                    .Setup(p => p.ExistsAsync(_id, _version, _cancellationToken))
                    .ReturnsAsync(false);

                using (var downloadStream = new MemoryStream())
                {
                    _upstream
                        .Setup(u => u.DownloadPackageOrNullAsync(_id, _version, _cancellationToken))
                        .ReturnsAsync(downloadStream);

                    await TargetAsync();

                    _indexer.Verify(
                        i => i.IndexAsync(It.IsAny<Stream>(), _cancellationToken),
                        Times.Once);
                }
            }
        }

        public class AddDownloadAsync : FactsBase
        {
            [Fact]
            public async Task AddsDownload()
            {
                var id = "Hello";
                var version = new NuGetVersion("1.2.3");

                await _target.AddDownloadAsync(id, version, _cancellationToken);

                _db.Verify(
                    db => db.AddDownloadAsync(id, version, _cancellationToken),
                    Times.Once);
            }
        }

        public class FactsBase
        {
            protected readonly Mock<IPackageDatabase> _db;
            protected readonly Mock<IUpstreamClient> _upstream;
            protected readonly Mock<IPackageIndexingService> _indexer;

            protected readonly CancellationToken _cancellationToken = CancellationToken.None;
            protected readonly PackageService _target;

            protected FactsBase()
            {
                _db = new Mock<IPackageDatabase>();
                _upstream = new Mock<IUpstreamClient>();
                _indexer = new Mock<IPackageIndexingService>();

                _target = new PackageService(
                    _db.Object,
                    _upstream.Object,
                    _indexer.Object,
                    Mock.Of<ILogger<PackageService>>());
            }
        }
    }
}
