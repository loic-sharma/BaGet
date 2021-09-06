using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol;
using BaGet.Protocol.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Mirror
{
    public class MirrorServiceTests
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

                _packages
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
                Setup(upstreamPackages: new List<PackageMetadata>
                {
                    new PackageMetadata { Version = "1.0.0" },
                    new PackageMetadata { Version = "2.0.0" },
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
                    upstreamPackages: new List<PackageMetadata>
                    {
                        new PackageMetadata { Version = "2.0.0" },
                        new PackageMetadata { Version = "3.0.0" },
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
                IReadOnlyList<PackageMetadata> upstreamPackages = null)
            {
                localPackages = localPackages ?? new List<Package>();
                upstreamPackages = upstreamPackages ?? new List<PackageMetadata>();

                _packages
                    .Setup(p => p.FindAsync(
                        "MyPackage",
                        /*includeUnlisted: */ true,
                        _cancellationToken))
                    .ReturnsAsync(localPackages);

                _upstream
                    .Setup(u => u.GetPackageMetadataAsync(
                        "MyPackage",
                        _cancellationToken))
                    .ReturnsAsync(upstreamPackages);
            }
        }

        public class MirrorAsync : FactsBase
        {
            [Fact]
            public async Task SkipsIfAlreadyMirrored()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task SkipsIfUpstreamDoesntHavePackage()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task MirrorsPackage()
            {
                await Task.Yield();
            }
        }

        public class FactsBase
        {
            protected readonly Mock<IPackageService> _packages;
            protected readonly Mock<IMirrorClient> _upstream;
            protected readonly Mock<IPackageIndexingService> _indexer;
            protected readonly CancellationToken _cancellationToken = CancellationToken.None;

            protected readonly MirrorService _target;

            public FactsBase()
            {
                _packages = new Mock<IPackageService>();
                _upstream = new Mock<IMirrorClient>();
                _indexer = new Mock<IPackageIndexingService>();

                _target = new MirrorService(
                    _packages.Object,
                    _upstream.Object,
                    _indexer.Object,
                    Mock.Of<ILogger<MirrorService>>());
            }
        }
    }
}
