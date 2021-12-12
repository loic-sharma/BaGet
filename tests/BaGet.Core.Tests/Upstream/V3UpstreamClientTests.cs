using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol;
using BaGet.Protocol.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests
{
    public class V3UpstreamClientTests
    {
        public class ListPackageVersionsAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsEmpty()
            {
                _client
                    .Setup(c => c.ListPackageVersionsAsync(
                        _id,
                        /*includeUnlisted: */ true,
                        _cancellation))
                    .ReturnsAsync(new List<NuGetVersion>());

                var result = await _target.ListPackageVersionsAsync(_id, _cancellation);

                Assert.Empty(result);
            }

            [Fact]
            public async Task IgnoresExceptions()
            {
                _client
                    .Setup(c => c.ListPackageVersionsAsync(
                        _id,
                        /*includeUnlisted: */ true,
                        _cancellation))
                    .ThrowsAsync(new InvalidDataException("Hello"));

                var result = await _target.ListPackageVersionsAsync(_id, _cancellation);

                Assert.Empty(result);
            }

            [Fact]
            public async Task ReturnsPackages()
            {
                _client
                    .Setup(c => c.ListPackageVersionsAsync(
                        _id,
                        /*includeUnlisted: */ true,
                        _cancellation))
                    .ReturnsAsync(new List<NuGetVersion> { _version });

                var result = await _target.ListPackageVersionsAsync(_id, _cancellation);

                var version = Assert.Single(result);
                Assert.Equal(_version, version);
            }
        }

        public class ListPackagesAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsEmpty()
            {
                _client
                    .Setup(c => c.GetPackageMetadataAsync(_id, _cancellation))
                    .ReturnsAsync(new List<PackageMetadata>());

                var result = await _target.ListPackagesAsync(_id, _cancellation);

                Assert.Empty(result);
            }

            [Fact]
            public async Task IgnoresExceptions()
            {
                _client
                    .Setup(c => c.GetPackageMetadataAsync(_id, _cancellation))
                    .ThrowsAsync(new InvalidDataException("Hello world"));

                var result = await _target.ListPackagesAsync(_id, _cancellation);

                Assert.Empty(result);
            }

            [Fact]
            public async Task ReturnsPackages()
            {
                var published = DateTimeOffset.Now;

                _client
                    .Setup(c => c.GetPackageMetadataAsync(_id, _cancellation))
                    .ReturnsAsync(new List<PackageMetadata>
                    {
                        new PackageMetadata
                        {
                            PackageId = "Foo",
                            Version = "1.2.3-prerelease+semver2",
                            Authors = "Author1, Author2",
                            Description = "Description",
                            IconUrl = "https://icon.test/",
                            Language = "Language",
                            LicenseUrl = "https://license.test/",
                            Listed = true,
                            MinClientVersion = "1.0.0",
                            PackageContentUrl = "https://content.test/",
                            Published = published,
                            RequireLicenseAcceptance = true,
                            Summary = "Summary",
                            Title = "Title",

                            Tags = new List<string> { "Tag1", "Tag2" },

                            Deprecation = new PackageDeprecation
                            {
                                Reasons = new List<string> { "Reason1", "Reason2" },
                                Message = "Message",
                                AlternatePackage = new AlternatePackage
                                {
                                    Id = "Alternate",
                                    Range = "*",
                                },
                            },
                            DependencyGroups = new List<DependencyGroupItem>
                            {
                                new DependencyGroupItem
                                {
                                    TargetFramework = "Target Framework",
                                    Dependencies = new List<DependencyItem>
                                    {
                                        new DependencyItem
                                        {
                                            Id = "Dependency",
                                            Range = "1.0.0",
                                        }
                                    }
                                }
                            }
                        }
                    });

                var result = await _target.ListPackagesAsync(_id, _cancellation);

                var package = Assert.Single(result);

                Assert.Equal("Foo", package.Id);
                Assert.Equal(new[] { "Author1", "Author2"}, package.Authors);
                Assert.Equal("Description", package.Description);
                Assert.False(package.HasReadme);
                Assert.False(package.HasEmbeddedIcon);
                Assert.True(package.IsPrerelease);
                Assert.Null(package.ReleaseNotes);
                Assert.Equal("Language", package.Language);
                Assert.True(package.Listed);
                Assert.Equal("1.0.0", package.MinClientVersion);
                Assert.Equal(published.UtcDateTime, package.Published);
                Assert.True(package.RequireLicenseAcceptance);
                Assert.Equal(SemVerLevel.SemVer2, package.SemVerLevel);
                Assert.Equal("Summary", package.Summary);
                Assert.Equal("Title", package.Title);
                Assert.Equal("https://icon.test/", package.IconUrlString);
                Assert.Equal("https://license.test/", package.LicenseUrlString);
                Assert.Equal("", package.ProjectUrlString);
                Assert.Equal("", package.RepositoryUrlString);
                Assert.Null(package.RepositoryType);
                Assert.Equal(new[] { "Tag1", "Tag2" }, package.Tags);
                Assert.Equal("1.2.3-prerelease", package.NormalizedVersionString);
                Assert.Equal("1.2.3-prerelease+semver2", package.OriginalVersionString);
            }
        }

        public class DownloadPackageOrNullAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsNull()
            {
                _client
                    .Setup(c => c.DownloadPackageAsync(_id, _version, _cancellation))
                    .ThrowsAsync(new PackageNotFoundException(_id, _version));

                var result = await _target.DownloadPackageOrNullAsync(_id, _version, _cancellation);

                Assert.Null(result);
            }

            [Fact]
            public async Task IgnoresExceptions()
            {
                _client
                    .Setup(c => c.DownloadPackageAsync(_id, _version, _cancellation))
                    .ThrowsAsync(new InvalidDataException("Hello world"));

                var result = await _target.DownloadPackageOrNullAsync(_id, _version, _cancellation);

                Assert.Null(result);
            }

            [Fact]
            public async Task ReturnsPackage()
            {
                _client
                    .Setup(c => c.DownloadPackageAsync(_id, _version, _cancellation))
                    .ReturnsAsync(new MemoryStream());

                var result = await _target.DownloadPackageOrNullAsync(_id, _version, _cancellation);

                Assert.NotNull(result);
                Assert.True(result.CanSeek);
            }
        }

        public class FactsBase
        {
            protected readonly Mock<NuGetClient> _client;
            protected readonly V3UpstreamClient _target;

            protected readonly string _id = "Foo";
            protected readonly NuGetVersion _version = new NuGetVersion("1.2.3-prerelease+semver2");
            protected readonly CancellationToken _cancellation = CancellationToken.None;

            protected FactsBase()
            {
                _client = new Mock<NuGetClient>();
                _target = new V3UpstreamClient(
                    _client.Object,
                    Mock.Of<ILogger<V3UpstreamClient>>());
            }
        }
    }
}
