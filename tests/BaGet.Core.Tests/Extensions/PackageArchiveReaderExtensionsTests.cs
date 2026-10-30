using System.Collections.Generic;
using System.Linq;
using BaGet.Core.Tests.Support;
using NuGet.Packaging;
using Xunit;

namespace BaGet.Core.Tests.Extensions;

public class PackageArchiveReaderExtensionsTests
{
    private static Package Read(string extraMetadata = "", IDictionary<string, string>? files = null)
    {
        using var stream = TestPackage.CreateMinimal(extraMetadata, files: files);
        using var reader = new PackageArchiveReader(stream);

        return reader.GetPackageMetadata();
    }

    public class Identity
    {
        [Fact]
        public void ReadsTheIdAndVersion()
        {
            using var stream = TestPackage.CreateMinimal(id: "My.Package", version: "2.3.4");
            using var reader = new PackageArchiveReader(stream);

            var package = reader.GetPackageMetadata();

            Assert.Equal("My.Package", package.Id);
            Assert.Equal("2.3.4", package.Version.ToNormalizedString());
        }

        [Fact]
        public void MarksAPrereleaseVersion()
        {
            using var stream = TestPackage.CreateMinimal(version: "1.0.0-beta1");
            using var reader = new PackageArchiveReader(stream);

            Assert.True(reader.GetPackageMetadata().IsPrerelease);
        }

        [Fact]
        public void AReleaseVersionIsNotPrerelease()
        {
            Assert.False(Read().IsPrerelease);
        }

        [Fact]
        public void ListsThePackageByDefault()
        {
            Assert.True(Read().Listed);
        }
    }

    public class Authors
    {
        [Fact]
        public void SplitsOnCommas()
        {
            using var stream = TestPackage.CreateMinimal(authors: "First,Second");
            using var reader = new PackageArchiveReader(stream);

            Assert.Equal(new[] { "First", "Second" }, reader.GetPackageMetadata().Authors);
        }

        [Fact]
        public void SplitsOnSemicolons()
        {
            using var stream = TestPackage.CreateMinimal(authors: "First;Second");
            using var reader = new PackageArchiveReader(stream);

            Assert.Equal(new[] { "First", "Second" }, reader.GetPackageMetadata().Authors);
        }

        [Fact]
        public void KeepsSpacesInsideAName()
        {
            // An author's name is not space delimited, unlike tags.
            using var stream = TestPackage.CreateMinimal(authors: "Ada Lovelace,Alan Turing");
            using var reader = new PackageArchiveReader(stream);

            Assert.Equal(
                new[] { "Ada Lovelace", "Alan Turing" },
                reader.GetPackageMetadata().Authors);
        }

        [Fact]
        public void DropsEmptyEntries()
        {
            using var stream = TestPackage.CreateMinimal(authors: "First,,Second,");
            using var reader = new PackageArchiveReader(stream);

            Assert.Equal(new[] { "First", "Second" }, reader.GetPackageMetadata().Authors);
        }
    }

    public class Tags
    {
        [Fact]
        public void AreEmptyWhenAbsent()
        {
            #pragma warning disable CS8604
            Assert.Empty(Read().Tags);
            #pragma warning restore CS8604
        }

        [Fact]
        public void SplitOnSpaces()
        {
            Assert.Equal(
                new[] { "one", "two", "three" },
                Read("    <tags>one two three</tags>").Tags);
        }

        [Fact]
        public void SplitOnCommasAndSemicolons()
        {
            Assert.Equal(
                new[] { "one", "two", "three" },
                Read("    <tags>one,two;three</tags>").Tags);
        }

        [Fact]
        public void DropEmptyEntries()
        {
            Assert.Equal(
                new[] { "one", "two" },
                Read("    <tags>one   two  </tags>").Tags);
        }
    }

    public class OptionalStrings
    {
        [Fact]
        public void AreEmptyRatherThanNullWhenAbsent()
        {
            var package = Read();

            Assert.Equal(string.Empty, package.Language);
            Assert.Equal(string.Empty, package.ReleaseNotes);
            Assert.Equal(string.Empty, package.MinClientVersion);
        }

        [Fact]
        public void AreReadWhenPresent()
        {
            var package = Read(
                @"    <language>en-GB</language>
    <releaseNotes>Notes here</releaseNotes>
    <summary>A summary</summary>
    <title>A title</title>");

            Assert.Equal("en-GB", package.Language);
            Assert.Equal("Notes here", package.ReleaseNotes);
            Assert.Equal("A summary", package.Summary);
            Assert.Equal("A title", package.Title);
        }

        [Fact]
        public void RequireLicenseAcceptanceDefaultsToFalse()
        {
            Assert.False(Read().RequireLicenseAcceptance);
        }

        [Fact]
        public void RequireLicenseAcceptanceIsRead()
        {
            Assert.True(Read("    <requireLicenseAcceptance>true</requireLicenseAcceptance>")
                .RequireLicenseAcceptance);
        }
    }

    public class Urls
    {
        [Fact]
        public void AreNullWhenAbsent()
        {
            var package = Read();

            Assert.Null(package.IconUrl);
            Assert.Null(package.LicenseUrl);
            Assert.Null(package.ProjectUrl);
        }

        [Fact]
        public void AreReadWhenAbsolute()
        {
            var package = Read(
                @"    <iconUrl>https://example.test/icon.png</iconUrl>
    <licenseUrl>https://example.test/licence</licenseUrl>
    <projectUrl>https://example.test/</projectUrl>");

            Assert.Equal("https://example.test/icon.png", package.IconUrl?.AbsoluteUri);
            Assert.Equal("https://example.test/licence", package.LicenseUrl?.AbsoluteUri);
            Assert.Equal("https://example.test/", package.ProjectUrl?.AbsoluteUri);
        }

        [Fact]
        public void ARelativeIconUrlIsIgnoredRatherThanFatal()
        {
            // Older packages carry a relative iconUrl. It cannot be served, but it must
            // not stop the package being read: the alternative is that pushing such a
            // package fails with an unhandled UriFormatException.
            var package = Read("    <iconUrl>images/icon.png</iconUrl>");

            Assert.Null(package.IconUrl);
        }

        [Fact]
        public void AMalformedProjectUrlIsIgnoredRatherThanFatal()
        {
            var package = Read("    <projectUrl>not a url at all</projectUrl>");

            Assert.Null(package.ProjectUrl);
        }
    }

    public class Repository
    {
        [Fact]
        public void IsNullWhenAbsent()
        {
            var package = Read();

            Assert.Null(package.RepositoryUrl);
            Assert.Null(package.RepositoryType);
        }

        [Fact]
        public void IsReadForAnHttpsUrl()
        {
            var package = Read(
                @"    <repository type=""git"" url=""https://example.test/repo"" />");

            Assert.Equal("https://example.test/repo", package.RepositoryUrl?.AbsoluteUri);
            Assert.Equal("git", package.RepositoryType);
        }

        [Fact]
        public void IsRejectedForAPlainHttpUrl()
        {
            var package = Read(
                @"    <repository type=""git"" url=""http://example.test/repo"" />");

            Assert.Null(package.RepositoryUrl);
            Assert.Null(package.RepositoryType);
        }

        [Fact]
        public void IsRejectedForANonAbsoluteUrl()
        {
            var package = Read(@"    <repository type=""git"" url=""../repo"" />");

            Assert.Null(package.RepositoryUrl);
        }

        [Fact]
        public void AMissingTypeIsNotFatal()
        {
            // The type attribute is optional; only the URL is required.
            var package = Read(@"    <repository url=""https://example.test/repo"" />");

            Assert.Equal("https://example.test/repo", package.RepositoryUrl?.AbsoluteUri);
        }
    }

    public class Dependencies
    {
        [Fact]
        public void AreEmptyWhenAbsent()
        {
            #pragma warning disable CS8604
            Assert.Empty(Read().Dependencies);
            #pragma warning restore CS8604
        }

        [Fact]
        public void AreReadWithTheirRangeAndFramework()
        {
            var package = Read(
                @"    <dependencies>
      <group targetFramework="".NETStandard2.0"">
        <dependency id=""Other.Package"" version=""1.2.3"" />
      </group>
    </dependencies>");

            #pragma warning disable CS8604
            var dependency = Assert.Single(package.Dependencies);
            #pragma warning restore CS8604
            Assert.Equal("Other.Package", dependency.Id);
            Assert.Equal("netstandard2.0", dependency.TargetFramework);
            Assert.Contains("1.2.3", dependency.VersionRange);
        }

        [Fact]
        public void AnEmptyGroupIsRecordedAsAFrameworkWithNoPackage()
        {
            var package = Read(
                @"    <dependencies>
      <group targetFramework="".NETStandard2.0"" />
    </dependencies>");

            #pragma warning disable CS8604
            var dependency = Assert.Single(package.Dependencies);
            #pragma warning restore CS8604
            Assert.Null(dependency.Id);
            Assert.Null(dependency.VersionRange);
            Assert.Equal("netstandard2.0", dependency.TargetFramework);
        }

        [Fact]
        public void EveryGroupIsRead()
        {
            var package = Read(
                @"    <dependencies>
      <group targetFramework="".NETStandard2.0"">
        <dependency id=""A"" version=""1.0.0"" />
      </group>
      <group targetFramework=""net472"">
        <dependency id=""B"" version=""2.0.0"" />
      </group>
    </dependencies>");

            Assert.Equal(new[] { "A", "B" }, package.Dependencies?.Select(d => d.Id));
        }
    }

    public class PackageTypesAndFrameworks
    {
        [Fact]
        public void DefaultsToTheDependencyPackageType()
        {
            #pragma warning disable CS8604
            var packageType = Assert.Single(Read().PackageTypes);
            #pragma warning restore CS8604

            Assert.Equal("Dependency", packageType.Name);
        }

        [Fact]
        public void ReadsADeclaredPackageType()
        {
            var package = Read(
                @"    <packageTypes>
      <packageType name=""DotnetTool"" />
    </packageTypes>");

            #pragma warning disable CS8604
            Assert.Equal("DotnetTool", Assert.Single(package.PackageTypes).Name);
            #pragma warning restore CS8604
        }

        [Fact]
        public void DefaultsToTheAnyFrameworkWhenThereAreNoLibraries()
        {
            #pragma warning disable CS8604
            var targetFramework = Assert.Single(Read().TargetFrameworks);
            #pragma warning restore CS8604

            Assert.Equal("any", targetFramework.Moniker);
        }

        [Fact]
        public void ReadsTheFrameworkOfALibraryFolder()
        {
            var package = Read(files: new Dictionary<string, string>
            {
                { "lib/netstandard2.0/Library.dll", "not a real assembly" },
            });

            #pragma warning disable CS8604
            Assert.Equal("netstandard2.0", Assert.Single(package.TargetFrameworks).Moniker);
            #pragma warning restore CS8604
        }
    }

    public class SemVerLevel
    {
        [Fact]
        public void IsUnknownForASemVer1Version()
        {
            using var stream = TestPackage.CreateMinimal(version: "1.0.0");
            using var reader = new PackageArchiveReader(stream);

            Assert.Equal(Core.SemVerLevel.Unknown, reader.GetPackageMetadata().SemVerLevel);
        }

        [Fact]
        public void IsSemVer2ForASemVer2Version()
        {
            using var stream = TestPackage.CreateMinimal(version: "1.0.0-beta.1");
            using var reader = new PackageArchiveReader(stream);

            Assert.Equal(Core.SemVerLevel.SemVer2, reader.GetPackageMetadata().SemVerLevel);
        }

        [Fact]
        public void IsSemVer2WhenADependencyRangeIsSemVer2()
        {
            var package = Read(
                @"    <dependencies>
      <group targetFramework="".NETStandard2.0"">
        <dependency id=""Other.Package"" version=""1.0.0-beta.1"" />
      </group>
    </dependencies>");

            Assert.Equal(Core.SemVerLevel.SemVer2, package.SemVerLevel);
        }
    }

    public class ReadmeAndIcon
    {
        [Fact]
        public void AreAbsentFromAMinimalPackage()
        {
            var package = Read();

            Assert.False(package.HasReadme);
            Assert.False(package.HasEmbeddedIcon);
        }

        [Fact]
        public void AreDetectedWhenDeclared()
        {
            var package = Read(
                @"    <readme>README.md</readme>
    <icon>icon.png</icon>",
                new Dictionary<string, string>
                {
                    { "README.md", "# Readme" },
                    { "icon.png", "not a real png" },
                });

            Assert.True(package.HasReadme);
            Assert.True(package.HasEmbeddedIcon);
        }
    }
}