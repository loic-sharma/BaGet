using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NuGet.Versioning;
using Xunit;
using BaGet.Web.Application.Screens;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.Tests;

public class PackageUseCaseFacts
{
    private readonly Mock<IPackageContentService> _content;
    private readonly Mock<IPackageService> _packages;
    private readonly Mock<ISearchService> _search;
    private readonly Mock<IUrlGenerator> _url;
    private readonly PackageUseCase _workflow;
    private PackageScreen _target = null!;

    private readonly CancellationToken _cancellation = CancellationToken.None;

    public PackageUseCaseFacts()
    {
        _content = new Mock<IPackageContentService>();
        _packages = new Mock<IPackageService>();
        _search = new Mock<ISearchService>();
        _url = new Mock<IUrlGenerator>();
        _workflow = new PackageUseCase(
            _packages.Object,
            _content.Object,
            _search.Object,
            _url.Object,
            new ServiceCollection().BuildServiceProvider());

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

        _target = (await _workflow.GetAsync("testpackage", "1.0.0", null, _cancellation)).Model!;

        Assert.False(_target.Found);
        #pragma warning disable CS8602
        Assert.Equal("testpackage", _target.Package.Id);
        #pragma warning restore CS8602
        Assert.Empty(_target.DependencyGroups);
        Assert.Empty(_target.Versions);
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

        _target = (await _workflow.GetAsync("testpackage", null, null, _cancellation)).Model!;

        Assert.False(_target.Found);
        #pragma warning disable CS8602
        Assert.Equal("testpackage", _target.Package.Id);
        #pragma warning restore CS8602
        Assert.Empty(_target.DependencyGroups);
        Assert.Empty(_target.Versions);
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

        _target = (await _workflow.GetAsync("testpackage", "2.0.0", null, _cancellation)).Model!;

        Assert.True(_target.Found);
        #pragma warning disable CS8602
        Assert.Equal("testpackage", _target.Package.Id);
        #pragma warning restore CS8602
        Assert.Equal("2.0.0", _target.Package.Version);

        #pragma warning disable CS8602
        Assert.Equal(3, _target.Versions.Count);
        Assert.Equal("3.0.0", _target.Versions[0].Version);
        #pragma warning restore CS8602
        Assert.False(_target.Versions[0].Selected);
        #pragma warning disable CS8602
        Assert.Equal("2.0.0", _target.Versions[1].Version);
        #pragma warning restore CS8602
        Assert.True(_target.Versions[1].Selected);
        #pragma warning disable CS8602
        Assert.Equal("1.0.0", _target.Versions[2].Version);
        #pragma warning restore CS8602
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

        _target = (await _workflow.GetAsync("testpackage", "2.0.0", null, _cancellation)).Model!;

        Assert.True(_target.Found);
        #pragma warning disable CS8602
        Assert.Equal("testpackage", _target.Package.Id);
        #pragma warning restore CS8602
        Assert.Equal("2.0.0", _target.Package.Version);

        #pragma warning disable CS8602
        Assert.Equal(2, _target.Versions.Count);
        Assert.Equal("3.0.0", _target.Versions[0].Version);
        #pragma warning restore CS8602
        Assert.False(_target.Versions[0].Selected);
        #pragma warning disable CS8602
        Assert.Equal("1.0.0", _target.Versions[1].Version);
        #pragma warning restore CS8602
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

        _target = (await _workflow.GetAsync("testpackage", "4.0.0", null, _cancellation)).Model!;

        Assert.True(_target.Found);
        #pragma warning disable CS8602
        Assert.Equal("testpackage", _target.Package.Id);
        #pragma warning restore CS8602
        Assert.Equal("2.0.0", _target.Package.Version);

        #pragma warning disable CS8602
        Assert.Equal(2, _target.Versions.Count);
        Assert.Equal("2.0.0", _target.Versions[0].Version);
        #pragma warning restore CS8602
        Assert.True(_target.Versions[0].Selected);
        #pragma warning disable CS8602
        Assert.Equal("1.0.0", _target.Versions[1].Version);
        #pragma warning restore CS8602
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

        _target = (await _workflow.GetAsync("testpackage", "1.0.0", null, _cancellation)).Model!;

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
                    new() { Id = "Used by 1" },
                    new() { Id = "Used by 2" },
                }
            });

        _target = (await _workflow.GetAsync("testpackage", "1.0.0", null, _cancellation)).Model!;

        #pragma warning disable CS8602
        Assert.Equal(2, _target.UsedBy.Count);
        #pragma warning restore CS8602
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

        _target = (await _workflow.GetAsync("testpackage", "1.0.0", null, _cancellation)).Model!;

        Assert.True(_target.Found);
        #pragma warning disable CS8602
        Assert.Equal(2, _target.DependencyGroups.Count);
        #pragma warning restore CS8602
        Assert.Equal(".NET 5.0", _target.DependencyGroups[0].Name);
        Assert.Equal(".NET Framework 4.8", _target.DependencyGroups[1].Name);

        #pragma warning disable CS8602
        Assert.Equal(2, _target.DependencyGroups[0].Dependencies.Count);
        #pragma warning restore CS8602
        #pragma warning disable CS8604
        Assert.Single(_target.DependencyGroups[1].Dependencies);
        #pragma warning restore CS8604

        #pragma warning disable CS8602
        Assert.Equal("Dependency1", _target.DependencyGroups[0].Dependencies[0].PackageId);
        Assert.Equal("(>= 1.0.0)", _target.DependencyGroups[0].Dependencies[0].VersionSpec);
        #pragma warning restore CS8602

        #pragma warning disable CS8602
        Assert.Equal("Dependency3", _target.DependencyGroups[0].Dependencies[1].PackageId);
        Assert.Equal("(>= 3.0.0)", _target.DependencyGroups[0].Dependencies[1].VersionSpec);
        #pragma warning restore CS8602

        #pragma warning disable CS8602
        Assert.Equal("Dependency2", _target.DependencyGroups[1].Dependencies[0].PackageId);
        Assert.Equal("(>= 2.0.0)", _target.DependencyGroups[1].Dependencies[0].VersionSpec);
        #pragma warning restore CS8602
    }

    [Theory]
    [InlineData(null, "All Frameworks")]
    [InlineData("net5.0", ".NET 5.0")]
    [InlineData("netstandard2.1", ".NET Standard 2.1")]
    [InlineData("net10.0", ".NET 10.0")]
    [InlineData("net4.8", ".NET Framework 4.8")]
    public async Task PrettifiesTargetFramework(string? targetFramework, string expectedResult)
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

        _target = (await _workflow.GetAsync("testpackage", "1.0.0", null, _cancellation)).Model!;

        Assert.True(_target.Found);
        #pragma warning disable CS8604
        var group = Assert.Single(_target.DependencyGroups);
        #pragma warning restore CS8604
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

        _target = (await _workflow.GetAsync("testpackage", "1.0.0", null, _cancellation)).Model!;

        Assert.True(_target.Found);
        Assert.Equal(15, _target.TotalDownloads);
        Assert.Equal(now, _target.LastUpdated);
    }

    [Fact]
    public async Task RendersReadme()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using (var readmeStream = new MemoryStream())
        {
            using (var streamWriter = new StreamWriter(readmeStream, leaveOpen: true))
            {
                await streamWriter.WriteLineAsync("# My readme");
                await streamWriter.WriteLineAsync("Hello world!");
                await streamWriter.FlushAsync(cancellationToken);
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

            _target = (await _workflow.GetAsync("testpackage", "1.0.0", null, _cancellation)).Model!;

            #pragma warning disable CS8602
            Assert.Equal(
                "<h1 id=\"my-readme\">My readme</h1>\n<p>Hello world!</p>\n",
                _target.ReadmeHtml);
            #pragma warning restore CS8602
        }
    }

    private Package CreatePackage(
        string version,
        long downloads = 0,
        bool hasReadme = false,
        bool listed = true,
        DateTime? published = null,
        IEnumerable<PackageDependency>? dependencies = null,
        IEnumerable<string>? packageTypes = null)
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