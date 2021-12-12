using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Web.Tests
{
    public class PackageModelFacts
    {
        private readonly Mock<IPackageContentService> _content;
        private readonly Mock<IPackageService> _packages;
        private readonly Mock<ISearchService> _search;
        private readonly Mock<IUrlGenerator> _url;
        private readonly PackageModel _target;

        private readonly CancellationToken _cancellation = CancellationToken.None;

        public PackageModelFacts()
        {
            _content = new Mock<IPackageContentService>();
            _packages = new Mock<IPackageService>();
            _search = new Mock<ISearchService>();
            _url = new Mock<IUrlGenerator>();
            _target = new PackageModel(
                _packages.Object,
                _content.Object,
                _search.Object,
                _url.Object);

            _search
                .Setup(s => s.FindDependentsAsync("testpackage", _cancellation))
                .ReturnsAsync(new DependentsResponse());
        }

        [Fact]
        public async Task ReturnsNotFound()
        {
            _packages
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>());

            await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

            Assert.False(_target.Found);
            Assert.Equal("testpackage", _target.Package.Id);
            Assert.Null(_target.DependencyGroups);
            Assert.Null(_target.Versions);
        }

        [Fact]
        public async Task ReturnsNotFoundIfAllUnlisted()
        {
            _packages
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>
                {
                    CreatePackage("1.0.0", listed: false),
                });

            await _target.OnGetAsync("testpackage", version: null, _cancellation);

            Assert.False(_target.Found);
            Assert.Equal("testpackage", _target.Package.Id);
            Assert.Null(_target.DependencyGroups);
            Assert.Null(_target.Versions);
        }

        [Fact]
        public async Task ReturnsRequestedVersion()
        {
            _packages
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>
                {
                    CreatePackage("1.0.0"),
                    CreatePackage("2.0.0"),
                    CreatePackage("3.0.0"),
                });

            await _target.OnGetAsync("testpackage", "2.0.0", _cancellation);

            Assert.True(_target.Found);
            Assert.Equal("testpackage", _target.Package.Id);
            Assert.Equal("2.0.0", _target.Package.NormalizedVersionString);

            Assert.Equal(3, _target.Versions.Count);
            Assert.Equal("3.0.0", _target.Versions[0].Version.OriginalVersion);
            Assert.False(_target.Versions[0].Selected);
            Assert.Equal("2.0.0", _target.Versions[1].Version.OriginalVersion);
            Assert.True(_target.Versions[1].Selected);
            Assert.Equal("1.0.0", _target.Versions[2].Version.OriginalVersion);
            Assert.False(_target.Versions[2].Selected);
        }

        [Fact]
        public async Task ReturnsRequestedUnlistedVersion()
        {
            _packages
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>
                {
                    CreatePackage("1.0.0"),
                    CreatePackage("2.0.0", listed: false),
                    CreatePackage("3.0.0"),
                });

            await _target.OnGetAsync("testpackage", "2.0.0", _cancellation);

            Assert.True(_target.Found);
            Assert.Equal("testpackage", _target.Package.Id);
            Assert.Equal("2.0.0", _target.Package.NormalizedVersionString);

            Assert.Equal(2, _target.Versions.Count);
            Assert.Equal("3.0.0", _target.Versions[0].Version.OriginalVersion);
            Assert.False(_target.Versions[0].Selected);
            Assert.Equal("1.0.0", _target.Versions[1].Version.OriginalVersion);
            Assert.False(_target.Versions[1].Selected);
        }

        [Fact]
        public async Task FallsBackToLatestListedVersion()
        {
            _packages
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>
                {
                    CreatePackage("1.0.0"),
                    CreatePackage("2.0.0"),
                    CreatePackage("3.0.0", listed: false),
                });

            await _target.OnGetAsync("testpackage", "4.0.0", _cancellation);

            Assert.True(_target.Found);
            Assert.Equal("testpackage", _target.Package.Id);
            Assert.Equal("2.0.0", _target.Package.NormalizedVersionString);

            Assert.Equal(2, _target.Versions.Count);
            Assert.Equal("2.0.0", _target.Versions[0].Version.OriginalVersion);
            Assert.True(_target.Versions[0].Selected);
            Assert.Equal("1.0.0", _target.Versions[1].Version.OriginalVersion);
            Assert.False(_target.Versions[1].Selected);
        }

        [Theory]
        [InlineData(new[] { "test" }, /*expectDotnetTemplate: */ false, /*expectDotnetTool: */ false)]
        [InlineData(new[] { "template" }, /*expectDotnetTemplate: */ true, /*expectDotnetTool: */ false)]
        [InlineData(new[] { "dOtNeTtOoL" }, /*expectDotnetTemplate: */ false, /*expectDotnetTool: */ true)]

        [InlineData(new[] { "tEmPlAte", "dOtNeTtOoL" }, /*expectDotnetTemplate: */ true, /*expectDotnetTool: */ true)]
        public async Task HandlesPackageTypes(IEnumerable<string> packageTypes, bool expectDotnetTemplate, bool expectDotnetTool)
        {
            _packages
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>
                {
                    CreatePackage("1.0.0", packageTypes: packageTypes)
                });

            await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

            Assert.True(_target.Found);
            Assert.Equal(expectDotnetTemplate, _target.IsDotnetTemplate);
            Assert.Equal(expectDotnetTool, _target.IsDotnetTool);
        }

        [Fact]
        public async Task FindsDependentPackages()
        {
            _packages
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>
                {
                    CreatePackage("1.0.0")
                });

            _search
                .Setup(s => s.FindDependentsAsync("testpackage", _cancellation))
                .ReturnsAsync(new DependentsResponse
                {
                    Data = new List<PackageDependent>
                    {
                        new PackageDependent  { Id = "Used by 1" },
                        new PackageDependent  { Id = "Used by 2" },
                    }
                });

            await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

            Assert.Equal(2, _target.UsedBy.Count);
            Assert.Equal("Used by 1", _target.UsedBy[0].Id);
            Assert.Equal("Used by 2", _target.UsedBy[1].Id);
        }

        [Fact]
        public async Task GroupsVersions()
        {
            _packages
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>
                {
                    CreatePackage("1.0.0", dependencies: new[]
                    {
                        new PackageDependency
                        {
                            TargetFramework = "net5.0",
                            Id = "Dependency1",
                            VersionRange = "[1.0.0, )",
                        },
                        new PackageDependency
                        {
                            TargetFramework = "net4.8",
                            Id = "Dependency2",
                            VersionRange = "[2.0.0, )",
                        },
                        new PackageDependency
                        {
                            TargetFramework = "net5.0",
                            Id = "Dependency3",
                            VersionRange = "[3.0.0, )",
                        },
                    })
                });

            await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

            Assert.True(_target.Found);
            Assert.Equal(2, _target.DependencyGroups.Count);
            Assert.Equal(".NET 5.0", _target.DependencyGroups[0].Name);
            Assert.Equal(".NET Framework 4.8", _target.DependencyGroups[1].Name);

            Assert.Equal(2, _target.DependencyGroups[0].Dependencies.Count);
            Assert.Equal(1, _target.DependencyGroups[1].Dependencies.Count);

            Assert.Equal("Dependency1", _target.DependencyGroups[0].Dependencies[0].PackageId);
            Assert.Equal("(>= 1.0.0)", _target.DependencyGroups[0].Dependencies[0].VersionSpec);

            Assert.Equal("Dependency3", _target.DependencyGroups[0].Dependencies[1].PackageId);
            Assert.Equal("(>= 3.0.0)", _target.DependencyGroups[0].Dependencies[1].VersionSpec);

            Assert.Equal("Dependency2", _target.DependencyGroups[1].Dependencies[0].PackageId);
            Assert.Equal("(>= 2.0.0)", _target.DependencyGroups[1].Dependencies[0].VersionSpec);
        }

        [Theory]
        [InlineData(null, "All Frameworks")]
        [InlineData("net5.0", ".NET 5.0")]
        [InlineData("netstandard2.1", ".NET Standard 2.1")]
        [InlineData("netcoreapp3.1", ".NET Core 3.1")]
        [InlineData("net4.8", ".NET Framework 4.8")]
        public async Task PrettifiesTargetFramework(string targetFramework, string expectedResult)
        {
            _packages
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>
                {
                    CreatePackage("1.0.0", dependencies: new[]
                    {
                       new PackageDependency
                       {
                           TargetFramework = targetFramework,
                           Id = "DependencyPackage",
                           VersionRange = "[1.0.0, )",
                       }
                    })
                });

            await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

            Assert.True(_target.Found);
            var group = Assert.Single(_target.DependencyGroups);
            Assert.Equal(expectedResult, group.Name);
        }

        [Fact]
        public async Task StatisticsIncludeUnlistedPackages()
        {
            var now = DateTime.Now;

            _packages
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>
                {
                    CreatePackage("1.0.0", downloads: 10, published: DateTime.Now.AddDays(-2)),
                    CreatePackage("2.0.0", listed: false, downloads: 5, published: now),
                });

            await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

            Assert.True(_target.Found);
            Assert.Equal(15, _target.TotalDownloads);
            Assert.Equal(now, _target.LastUpdated);
        }

        [Fact]
        public async Task RendersReadme()
        {
            using (var readmeStream = new MemoryStream())
            {
                using (var streamWriter = new StreamWriter(readmeStream, leaveOpen: true))
                {
                    await streamWriter.WriteLineAsync("# My readme");
                    await streamWriter.WriteLineAsync("Hello world!");
                    await streamWriter.FlushAsync();
                }

                readmeStream.Position = 0;

                _packages
                    .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                    .ReturnsAsync(new List<Package>
                    {
                        CreatePackage("1.0.0", hasReadme: true),
                    });

                _content
                    .Setup(c => c.GetPackageReadmeStreamOrNullAsync(
                        "testpackage",
                        It.Is<NuGetVersion>(v => v.OriginalVersion == "1.0.0"),
                        _cancellation))
                    .ReturnsAsync(readmeStream);

                await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

                Assert.Equal(
                    "<h1 id=\"my-readme\">My readme</h1>\n<p>Hello world!</p>\n",
                    _target.Readme.Value);
            }
        }

        private Package CreatePackage(
            string version,
            long downloads = 0,
            bool hasReadme = false,
            bool listed = true,
            DateTime? published = null,
            IEnumerable<PackageDependency> dependencies = null,
            IEnumerable<string> packageTypes = null)
        {
            published = published ?? DateTime.Now;
            dependencies = dependencies ?? Array.Empty<PackageDependency>();
            packageTypes = packageTypes ?? Array.Empty<string>();

            return new Package
            {
                Id = "testpackage",
                Downloads = downloads,
                HasReadme = hasReadme,
                Listed = listed,
                NormalizedVersionString = version,
                Published = published.Value,

                Dependencies = dependencies.ToList(),
                PackageTypes = packageTypes
                    .Select(name => new PackageType { Name = name })
                    .ToList(),
            };
        }
    }
}
