using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Services;

public class PackageIndexingServiceTests
{
    private const string PackageId = "Test.Package";
    private const string PackageVersion = "1.0.0";

    private static readonly DateTime PublishedTime = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly Mock<IPackageDatabase> _packages;
    private readonly Mock<IPackageStorageService> _storage;
    private readonly Mock<ISearchIndexer> _search;
    private readonly Mock<SystemTime> _time;
    private readonly BaGetOptions _options;
    private readonly PackageIndexingService _target;

    private Package? _savedPackage;
    private byte[]? _savedNuspec;
    private byte[]? _savedReadme;
    private byte[]? _savedIcon;
    private bool _nuspecStreamWasNull;
    private bool _readmeStreamWasNull;
    private bool _iconStreamWasNull;

    public PackageIndexingServiceTests()
    {
        _packages = new Mock<IPackageDatabase>();
        _storage = new Mock<IPackageStorageService>();
        _search = new Mock<ISearchIndexer>();
        _time = new Mock<SystemTime>();
        _options = new BaGetOptions();

        _time.Setup(t => t.UtcNow).Returns(PublishedTime);

        _packages
            .Setup(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackageAddResult.Success);

        _storage
            .Setup(s => s.SavePackageContentAsync(
                It.IsAny<Package>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Callback<Package, Stream, Stream, Stream, Stream, CancellationToken>(
                (package, packageStream, nuspecStream, readmeStream, iconStream, _) =>
                {
                    // The streams are only guaranteed to be usable for the duration of the
                    // call, so their content is captured here instead of the streams themselves.
                    _savedPackage = package;
                    _nuspecStreamWasNull = nuspecStream == null;
                    _readmeStreamWasNull = readmeStream == null;
                    _iconStreamWasNull = iconStream == null;
                    _savedNuspec = ReadAllBytes(nuspecStream);
                    _savedReadme = ReadAllBytes(readmeStream);
                    _savedIcon = ReadAllBytes(iconStream);
                })
            .ReturnsAsync(PackageSaveResult.Success);

        var options = new Mock<IOptionsSnapshot<BaGetOptions>>();
        options.Setup(o => o.Value).Returns(_options);

        _target = new PackageIndexingService(
            _packages.Object,
            _storage.Object,
            _search.Object,
            _time.Object,
            options.Object,
            Mock.Of<ILogger<PackageIndexingService>>());
    }

    [Fact]
    public async Task WhenStreamIsNotAZipArchive_ReturnsInvalidPackage()
    {
        using (var stream = TestPackage.NotAZipArchive())
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.InvalidPackage, result);
            AssertNothingWasPersisted();
        }
    }

    [Fact]
    public async Task WhenPackageHasNoNuspec_ReturnsInvalidPackage()
    {
        using (var stream = TestPackage.CreateWithoutNuspec(
                   new Dictionary<string, string> { ["lib/net10.0/Test.dll"] = "not a real assembly" }))
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.InvalidPackage, result);
            AssertNothingWasPersisted();
        }
    }

    [Fact]
    public async Task WhenNuspecIsNotValidXml_ReturnsInvalidPackage()
    {
        // The unterminated element breaks the nuspec document as a whole.
        using (var stream = TestPackage.Create("    <id>Test.Package</id"))
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.InvalidPackage, result);
            AssertNothingWasPersisted();
        }
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    public async Task WhenNuspecIsMissingARequiredElement_ReturnsInvalidPackage(string missingElement)
    {
        using (var stream = TestPackage.Create(MetadataWithout(missingElement)))
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.InvalidPackage, result);
            AssertNothingWasPersisted();
        }
    }

    [Fact]
    public async Task WhenPackageDeclaresAReadmeThatIsNotInTheArchive_ReturnsInvalidPackage()
    {
        using (var stream = TestPackage.CreateMinimal(extraMetadata: "    <readme>readme.md</readme>"))
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.InvalidPackage, result);
            AssertNothingWasPersisted();
        }
    }

    [Theory]
    [InlineData("../escaped.dll")]
    [InlineData("../../escaped.dll")]
    [InlineData("lib/../../escaped.dll")]
    public async Task WhenPackageHasEntryThatExtractsOutsideOfExtractionPath_ReturnsInvalidPackage(string path)
    {
        using (var stream = TestPackage.CreateMinimal(
                   files: new Dictionary<string, string> { [path] = "escaped" }))
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.InvalidPackage, result);
            AssertNothingWasPersisted();
        }
    }

    [Fact]
    public async Task WhenPackageAlreadyExists_ReturnsPackageAlreadyExists()
    {
        _options.AllowPackageOverwrites = false;
        _packages
            .Setup(p => p.ExistsAsync(PackageId, NuGetVersion.Parse(PackageVersion), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        using (var stream = TestPackage.CreateMinimal())
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.PackageAlreadyExists, result);
            AssertNothingWasPersisted();
            _packages.Verify(
                p => p.HardDeletePackageAsync(
                    It.IsAny<string>(),
                    It.IsAny<NuGetVersion>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Fact]
    public async Task WhenPackageAlreadyExistsAndOverwritesAreAllowed_DeletesTheExistingPackageAndIndexes()
    {
        _options.AllowPackageOverwrites = true;
        _packages
            .Setup(p => p.ExistsAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        using (var stream = TestPackage.CreateMinimal())
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.Success, result);
            _packages.Verify(
                p => p.HardDeletePackageAsync(
                    PackageId,
                    NuGetVersion.Parse(PackageVersion),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _storage.Verify(
                s => s.DeleteAsync(
                    PackageId,
                    NuGetVersion.Parse(PackageVersion),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task WhenDatabaseAddFailsBecausePackageAlreadyExists_ReturnsPackageAlreadyExists()
    {
        _packages
            .Setup(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackageAddResult.PackageAlreadyExists);

        using (var stream = TestPackage.CreateMinimal())
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.PackageAlreadyExists, result);
            _search.Verify(
                s => s.IndexAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Fact]
    public async Task WhenDatabaseAddReturnsAnUnknownResult_Throws()
    {
        _packages
            .Setup(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackageAddResult)int.MaxValue);

        using (var stream = TestPackage.CreateMinimal())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _target.IndexAsync(stream, CancellationToken.None));

            _search.Verify(
                s => s.IndexAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Fact]
    public async Task IndexesPackage()
    {
        using (var stream = TestPackage.CreateMinimal(id: "My.Package", version: "1.2.3-beta.1"))
        {
            var expectedNuspec = ReadEntry(stream, TestPackage.NuspecPath);

            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.Success, result);

            Assert.NotNull(_savedPackage);
            Assert.Equal("My.Package", _savedPackage.Id);
            Assert.Equal(NuGetVersion.Parse("1.2.3-beta.1"), _savedPackage.Version);
            Assert.True(_savedPackage.IsPrerelease);
            Assert.Equal(PublishedTime, _savedPackage.Published);
            Assert.False(_nuspecStreamWasNull);
            Assert.Equal(expectedNuspec, _savedNuspec);

            _packages.Verify(
                p => p.AddAsync(
                    It.Is<Package>(x => x.Id == "My.Package" && x.Published == PublishedTime),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _search.Verify(
                s => s.IndexAsync(
                    It.Is<Package>(x => x.Id == "My.Package"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task SavesThePackageStreamFromItsBeginning()
    {
        byte[]? actual = null;
        long positionAtSave = -1;

        _storage
            .Setup(s => s.SavePackageContentAsync(
                It.IsAny<Package>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Callback<Package, Stream, Stream, Stream, Stream, CancellationToken>(
                (_, packageStream, __, ___, ____, _____) =>
                {
                    positionAtSave = packageStream.Position;

                    // Copied from wherever the service left the stream, not from the beginning.
                    using (var buffer = new MemoryStream())
                    {
                        packageStream.CopyTo(buffer);
                        actual = buffer.ToArray();
                    }
                })
            .ReturnsAsync(PackageSaveResult.Success);

        using (var stream = TestPackage.CreateMinimal())
        {
            var expected = stream.ToArray();

            // Leave the stream at its end: the service is responsible for rewinding it.
            stream.Position = stream.Length;

            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.Success, result);
            Assert.Equal(0, positionAtSave);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task WhenPackageHasNoReadme_SavesNullReadmeStream()
    {
        using (var stream = TestPackage.CreateMinimal())
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.Success, result);
            Assert.False(_savedPackage?.HasReadme);
            Assert.True(_readmeStreamWasNull);
            Assert.Null(_savedReadme);
        }
    }

    [Fact]
    public async Task WhenPackageHasReadme_SavesReadmeStream()
    {
        using (var stream = TestPackage.CreateMinimal(
                   extraMetadata: "    <readme>docs/readme.md</readme>",
                   files: new Dictionary<string, string> { ["docs/readme.md"] = "# Hello" }))
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.Success, result);
            Assert.True(_savedPackage?.HasReadme);
            Assert.False(_readmeStreamWasNull);
            #pragma warning disable CS8604
            Assert.Equal("# Hello", Encoding.UTF8.GetString(_savedReadme));
            #pragma warning restore CS8604
        }
    }

    [Fact]
    public async Task WhenPackageHasNoEmbeddedIcon_SavesNullIconStream()
    {
        using (var stream = TestPackage.CreateMinimal())
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.Success, result);
            Assert.False(_savedPackage?.HasEmbeddedIcon);
            Assert.True(_iconStreamWasNull);
            Assert.Null(_savedIcon);
        }
    }

    [Fact]
    public async Task WhenPackageHasEmbeddedIcon_SavesIconStream()
    {
        using (var stream = TestPackage.CreateMinimal(
                   extraMetadata: "    <icon>images/icon.png</icon>",
                   files: new Dictionary<string, string> { ["images/icon.png"] = "icon content" }))
        {
            var result = await _target.IndexAsync(stream, CancellationToken.None);

            Assert.Equal(PackageIndexingResult.Success, result);
            Assert.True(_savedPackage?.HasEmbeddedIcon);
            Assert.False(_iconStreamWasNull);
            #pragma warning disable CS8604
            Assert.Equal("icon content", Encoding.UTF8.GetString(_savedIcon));
            #pragma warning restore CS8604
        }
    }

    [Fact]
    public async Task ThrowsWhenStorageSaveThrows()
    {
        _storage
            .Setup(s => s.SavePackageContentAsync(
                It.IsAny<Package>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Concurrent push"));

        using (var stream = TestPackage.CreateMinimal())
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _target.IndexAsync(stream, CancellationToken.None));

            Assert.Equal("Concurrent push", exception.Message);
            _packages.Verify(
                p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _search.Verify(
                s => s.IndexAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    private void AssertNothingWasPersisted()
    {
        _storage.Verify(
            s => s.SavePackageContentAsync(
                It.IsAny<Package>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _packages.Verify(
            p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _search.Verify(
            s => s.IndexAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static string MetadataWithout(string element)
    {
        var metadata = new StringBuilder();

        if (element != "id") metadata.AppendLine($"    <id>{PackageId}</id>");
        if (element != "version") metadata.AppendLine($"    <version>{PackageVersion}</version>");
        if (element != "authors") metadata.AppendLine("    <authors>Test Author</authors>");
        if (element != "description") metadata.AppendLine("    <description>A test package.</description>");

        return metadata.ToString();
    }

    private static byte[]? ReadEntry(MemoryStream packageStream, string path)
    {
        var position = packageStream.Position;

        try
        {
            packageStream.Position = 0;

            using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true))
            using (var entry = archive.GetEntry(path)?.Open())
            {
                return ReadAllBytes(entry);
            }
        }
        finally
        {
            packageStream.Position = position;
        }
    }

    private static byte[]? ReadAllBytes(Stream? stream)
    {
        if (stream == null) return null;

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}