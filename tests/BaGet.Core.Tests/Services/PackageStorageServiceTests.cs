using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Services;

public class PackageStorageServiceTests
{
    public class SavePackageContentAsync : FactsBase
    {
        [Fact]
        public async Task ThrowsIfPackageIsNull()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _target.SavePackageContentAsync(
                    null,
                    packageStream: Stream.Null,
                    nuspecStream: Stream.Null,
                    readmeStream: Stream.Null,
                    iconStream: Stream.Null, cancellationToken));
        }

        [Fact]
        public async Task ThrowsIfPackageStreamIsNull()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _target.SavePackageContentAsync(
                    _package,
                    packageStream: null,
                    nuspecStream: Stream.Null,
                    readmeStream: Stream.Null,
                    iconStream: Stream.Null, cancellationToken));
        }

        [Fact]
        public async Task ThrowsIfNuspecStreamIsNull()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _target.SavePackageContentAsync(
                    _package,
                    packageStream: Stream.Null,
                    nuspecStream: null,
                    readmeStream: Stream.Null,
                    iconStream: Stream.Null, cancellationToken));
        }

        [Fact]
        public async Task SavesContent()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // Arrange
            SetupPutResult(StoragePutResult.Success);

            using (var packageStream = StringStream("My package"))
            using (var nuspecStream = StringStream("My nuspec"))
            using (var readmeStream = StringStream("My readme"))
            using (var iconStream = StringStream("My icon"))
            {
                // Act
                await _target.SavePackageContentAsync(
                    _package,
                    packageStream: packageStream,
                    nuspecStream: nuspecStream,
                    readmeStream: readmeStream,
                    iconStream: iconStream, cancellationToken);

                // Assert
                Assert.True(_puts.ContainsKey(PackagePath));
                Assert.Equal("My package", await ToStringAsync(_puts[PackagePath].Content));
                Assert.Equal("binary/octet-stream", _puts[PackagePath].ContentType);

                Assert.True(_puts.ContainsKey(NuspecPath));
                Assert.Equal("My nuspec", await ToStringAsync(_puts[NuspecPath].Content));
                Assert.Equal("text/plain", _puts[NuspecPath].ContentType);

                Assert.True(_puts.ContainsKey(ReadmePath));
                Assert.Equal("My readme", await ToStringAsync(_puts[ReadmePath].Content));
                Assert.Equal("text/markdown", _puts[ReadmePath].ContentType);

                Assert.True(_puts.ContainsKey(IconPath));
                Assert.Equal("My icon", await ToStringAsync(_puts[IconPath].Content));
                Assert.Equal("image/xyz", _puts[IconPath].ContentType);
            }
        }

        [Fact]
        public async Task DoesNotSaveReadmeIfItIsNull()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // Arrange
            SetupPutResult(StoragePutResult.Success);

            using (var packageStream = StringStream("My package"))
            using (var nuspecStream = StringStream("My nuspec"))
            {
                // Act
                await _target.SavePackageContentAsync(
                    _package,
                    packageStream: packageStream,
                    nuspecStream: nuspecStream,
                    readmeStream: null,
                    iconStream: null, cancellationToken);
            }

            // Assert
            Assert.False(_puts.ContainsKey(ReadmePath));
        }

        [Fact]
        public async Task NormalizesVersionWhenContentIsSaved()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // Arrange
            SetupPutResult(StoragePutResult.Success);

            _package.Version = new NuGetVersion("1.2.3.0");
            using (var packageStream = StringStream("My package"))
            using (var nuspecStream = StringStream("My nuspec"))
            using (var readmeStream = StringStream("My readme"))
            using (var iconStream = StringStream("My icon"))
            {
                // Act
                await _target.SavePackageContentAsync(
                    _package,
                    packageStream: packageStream,
                    nuspecStream: nuspecStream,
                    readmeStream: readmeStream,
                    iconStream: iconStream, cancellationToken);
            }

            // Assert
            Assert.True(_puts.ContainsKey(PackagePath));
            Assert.True(_puts.ContainsKey(NuspecPath));
            Assert.True(_puts.ContainsKey(ReadmePath));
        }

        [Fact]
        public async Task DoesNotThrowIfContentAlreadyExistsAndContentsMatch()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // Arrange
            SetupPutResult(StoragePutResult.AlreadyExists);

            using (var packageStream = StringStream("My package"))
            using (var nuspecStream = StringStream("My nuspec"))
            using (var readmeStream = StringStream("My readme"))
            using (var iconStream = StringStream("My icon"))
            {
                var result = await _target.SavePackageContentAsync(
                    _package,
                    packageStream: packageStream,
                    nuspecStream: nuspecStream,
                    readmeStream: readmeStream,
                    iconStream: iconStream, cancellationToken);

                // Assert
                Assert.Equal(PackageSaveResult.Success, result);

                Assert.True(_puts.ContainsKey(PackagePath));
                Assert.Equal("My package", await ToStringAsync(_puts[PackagePath].Content));
                Assert.Equal("binary/octet-stream", _puts[PackagePath].ContentType);

                Assert.True(_puts.ContainsKey(NuspecPath));
                Assert.Equal("My nuspec", await ToStringAsync(_puts[NuspecPath].Content));
                Assert.Equal("text/plain", _puts[NuspecPath].ContentType);

                Assert.True(_puts.ContainsKey(ReadmePath));
                Assert.Equal("My readme", await ToStringAsync(_puts[ReadmePath].Content));
                Assert.Equal("text/markdown", _puts[ReadmePath].ContentType);

                Assert.True(_puts.ContainsKey(IconPath));
                Assert.Equal("My icon", await ToStringAsync(_puts[IconPath].Content));
                Assert.Equal("image/xyz", _puts[IconPath].ContentType);
            }
        }

        [Fact]
        public async Task ReturnsConflictIfContentAlreadyExistsButContentsDoNotMatch()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // Arrange
            SetupPutResult(StoragePutResult.Conflict);

            using (var packageStream = StringStream("My package"))
            using (var nuspecStream = StringStream("My nuspec"))
            using (var readmeStream = StringStream("My readme"))
            using (var iconStream = StringStream("My icon"))
            {
                // Act
                var result = await _target.SavePackageContentAsync(
                    _package,
                    packageStream: packageStream,
                    nuspecStream: nuspecStream,
                    readmeStream: readmeStream,
                    iconStream: iconStream, cancellationToken);

                // Assert
                Assert.Equal(PackageSaveResult.Conflict, result);
            }
        }
    }

    public class GetPackageStreamAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullIfDirectoryIsMissing()
        {
            // Arrange
            var cancellationToken = CancellationToken.None;
            _storage
                .Setup(s => s.GetAsync(PackagePath, cancellationToken))
                .ThrowsAsync(new DirectoryNotFoundException());

            // Act
            var result = await _target.GetPackageStreamAsync(_package.Id, _package.Version, cancellationToken);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetsStream()
        {
            // Arrange
            var cancellationToken = CancellationToken.None;
            using (var packageStream = StringStream("My package"))
            {
                _storage
                    .Setup(s => s.GetAsync(PackagePath, cancellationToken))
                    .ReturnsAsync(packageStream);

                // Act
                var result = await _target.GetPackageStreamAsync(_package.Id, _package.Version, cancellationToken);

                // Assert
                Assert.Equal("My package", await ToStringAsync(result));

                _storage.Verify(s => s.GetAsync(PackagePath, cancellationToken), Times.Once);
            }
        }
    }

    public class GetNuspecStreamAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullIfDirectoryIsMissing()
        {
            // Arrange
            var cancellationToken = CancellationToken.None;
            _storage
                .Setup(s => s.GetAsync(NuspecPath, cancellationToken))
                .ThrowsAsync(new DirectoryNotFoundException());

            // Act
            var result = await _target.GetNuspecStreamAsync(_package.Id, _package.Version, cancellationToken);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetsStream()
        {
            // Arrange
            var cancellationToken = CancellationToken.None;
            using (var nuspecStream = StringStream("My nuspec"))
            {
                _storage
                    .Setup(s => s.GetAsync(NuspecPath, cancellationToken))
                    .ReturnsAsync(nuspecStream);

                // Act
                var result = await _target.GetNuspecStreamAsync(_package.Id, _package.Version, cancellationToken);

                // Assert
                Assert.Equal("My nuspec", await ToStringAsync(result));

                _storage.Verify(s => s.GetAsync(NuspecPath, cancellationToken), Times.Once);
            }
        }
    }

    public class GetReadmeStreamAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullIfDirectoryIsMissing()
        {
            // Arrange
            var cancellationToken = CancellationToken.None;
            _storage
                .Setup(s => s.GetAsync(ReadmePath, cancellationToken))
                .ThrowsAsync(new DirectoryNotFoundException());

            // Act
            var result = await _target.GetReadmeStreamAsync(_package.Id, _package.Version, cancellationToken);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetsStream()
        {
            // Arrange
            var cancellationToken = CancellationToken.None;
            using (var readmeStream = StringStream("My readme"))
            {
                _storage
                    .Setup(s => s.GetAsync(ReadmePath, cancellationToken))
                    .ReturnsAsync(readmeStream);

                // Act
                var result = await _target.GetReadmeStreamAsync(_package.Id, _package.Version, cancellationToken);

                // Assert
                Assert.Equal("My readme", await ToStringAsync(result));

                _storage.Verify(s => s.GetAsync(ReadmePath, cancellationToken), Times.Once);
            }
        }
    }

    public class DeleteAsync : FactsBase
    {
        [Fact]
        public async Task Deletes()
        {
            // Act
            var cancellationToken = CancellationToken.None;
            await _target.DeleteAsync(_package.Id, _package.Version, cancellationToken);

            _storage.Verify(s => s.DeleteAsync(PackagePath, cancellationToken), Times.Once);
            _storage.Verify(s => s.DeleteAsync(NuspecPath, cancellationToken), Times.Once);
            _storage.Verify(s => s.DeleteAsync(ReadmePath, cancellationToken), Times.Once);
        }
    }

    /// <summary>
    /// These facts run <see cref="PackageStorageService"/> on top of the real file system,
    /// through <see cref="FileStorageService"/>, the way <see cref="FileStorageServiceTests"/> does.
    /// </summary>
    public class SavePackageContentAsyncOnFileSystem : FileSystemFactsBase
    {
        [Fact]
        public async Task WhenContentIsNew_Succeeds()
        {
            // Act
            var result = await SaveAsync(
                package: "My package",
                nuspec: "My nuspec",
                readme: "My readme",
                icon: "My icon");

            // Assert
            Assert.Equal(PackageSaveResult.Success, result);
            Assert.Equal("My package", await ReadAsync(PackagePath));
            Assert.Equal("My nuspec", await ReadAsync(NuspecPath));
            Assert.Equal("My readme", await ReadAsync(ReadmePath));
            Assert.Equal("My icon", await ReadAsync(IconPath));
        }

        [Fact]
        public async Task WhenIdenticalContentIsAlreadyStored_Succeeds()
        {
            // Arrange
            await SaveAsync("My package", "My nuspec", "My readme", "My icon");

            // Act
            var result = await SaveAsync("My package", "My nuspec", "My readme", "My icon");

            // Assert
            Assert.Equal(PackageSaveResult.Success, result);
            Assert.Equal("My package", await ReadAsync(PackagePath));
        }

        [Fact]
        public async Task WhenStoredPackageContentDiffers_ReturnsConflict()
        {
            // Arrange
            await WriteAsync(PackagePath, "Other package");

            // Act
            var result = await SaveAsync("My package", "My nuspec", "My readme", "My icon");

            // Assert
            Assert.Equal(PackageSaveResult.Conflict, result);
            Assert.Equal("Other package", await ReadAsync(PackagePath));
            Assert.False(File.Exists(FullPath(NuspecPath)));
        }

        [Fact]
        public async Task WhenStoredNuspecContentDiffers_ReturnsConflict()
        {
            // Arrange
            await WriteAsync(NuspecPath, "Other nuspec");

            // Act
            var result = await SaveAsync("My package", "My nuspec", "My readme", "My icon");

            // Assert
            Assert.Equal(PackageSaveResult.Conflict, result);
            Assert.Equal("Other nuspec", await ReadAsync(NuspecPath));
            Assert.False(File.Exists(FullPath(ReadmePath)));
        }

        [Fact]
        public async Task WhenStoredReadmeContentDiffers_ReturnsConflict()
        {
            // Arrange
            await WriteAsync(ReadmePath, "Other readme");

            // Act
            var result = await SaveAsync("My package", "My nuspec", "My readme", "My icon");

            // Assert
            Assert.Equal(PackageSaveResult.Conflict, result);
            Assert.Equal("Other readme", await ReadAsync(ReadmePath));
            Assert.False(File.Exists(FullPath(IconPath)));
        }

        [Fact]
        public async Task WhenStoredIconContentDiffers_ReturnsConflict()
        {
            // Arrange
            await WriteAsync(IconPath, "Other icon");

            // Act
            var result = await SaveAsync("My package", "My nuspec", "My readme", "My icon");

            // Assert
            Assert.Equal(PackageSaveResult.Conflict, result);
            Assert.Equal("Other icon", await ReadAsync(IconPath));
        }

        [Fact]
        public async Task WhenPackageHasNoReadmeOrIcon_Succeeds()
        {
            // Act
            var result = await SaveAsync("My package", "My nuspec", readme: null, icon: null);

            // Assert
            Assert.Equal(PackageSaveResult.Success, result);
            Assert.False(File.Exists(FullPath(ReadmePath)));
            Assert.False(File.Exists(FullPath(IconPath)));
        }
    }

    public class FileSystemFactsBase : IDisposable
    {
        protected readonly Package _package = new()
        {
            Id = "My.Package",
            Version = new NuGetVersion("1.2.3")
        };

        private readonly string _storePath;
        private readonly PackageStorageService _target;

        public FileSystemFactsBase()
        {
            _storePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            var options = new Mock<IOptionsSnapshot<FileSystemStorageOptions>>();
            options
                .Setup(o => o.Value)
                .Returns(() => new FileSystemStorageOptions { Path = _storePath });

            _target = new PackageStorageService(
                new FileStorageService(options.Object),
                Mock.Of<ILogger<PackageStorageService>>());
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_storePath, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        protected string PackagePath => Path.Combine("packages", "my.package", "1.2.3", "my.package.1.2.3.nupkg");
        protected string NuspecPath => Path.Combine("packages", "my.package", "1.2.3", "my.package.nuspec");
        protected string ReadmePath => Path.Combine("packages", "my.package", "1.2.3", "readme");
        protected string IconPath => Path.Combine("packages", "my.package", "1.2.3", "icon");

        protected string FullPath(string path) => Path.Combine(_storePath, path);

        protected async Task<PackageSaveResult> SaveAsync(string? package, string? nuspec, string? readme, string? icon)
        {
            using (var packageStream = StringStream(package))
            using (var nuspecStream = StringStream(nuspec))
            using (var readmeStream = StringStream(readme))
            using (var iconStream = StringStream(icon))
            {
                return await _target.SavePackageContentAsync(
                    _package,
                    packageStream,
                    nuspecStream,
                    readmeStream,
                    iconStream,
                    CancellationToken.None);
            }
        }

        protected async Task WriteAsync(string path, string content)
        {
            var fullPath = FullPath(path);

            #pragma warning disable CS8604
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            #pragma warning restore CS8604
            await File.WriteAllTextAsync(fullPath, content);
        }

        protected Task<string> ReadAsync(string path)
        {
            return File.ReadAllTextAsync(FullPath(path));
        }

        private Stream? StringStream(string? input)
        {
            if (input == null) return null;

            return new MemoryStream(Encoding.ASCII.GetBytes(input));
        }
    }

    public class FactsBase
    {
        protected readonly Package _package = new()
        {
            Id = "My.Package",
            Version = new NuGetVersion("1.2.3")
        };

        protected readonly Mock<IStorageService> _storage;
        protected readonly PackageStorageService _target;

        protected readonly Dictionary<string, (Stream Content, string ContentType)> _puts;

        public FactsBase()
        {
            _storage = new Mock<IStorageService>();
            _target = new PackageStorageService(_storage.Object, Mock.Of<ILogger<PackageStorageService>>());

            _puts = new Dictionary<string, (Stream Content, string ContentType)>();
        }

        protected string PackagePath => Path.Combine("packages", "my.package", "1.2.3", "my.package.1.2.3.nupkg");
        protected string NuspecPath => Path.Combine("packages", "my.package", "1.2.3", "my.package.nuspec");
        protected string ReadmePath => Path.Combine("packages", "my.package", "1.2.3", "readme");
        protected string IconPath => Path.Combine("packages", "my.package", "1.2.3", "icon");

        protected Stream StringStream(string input)
        {
            var bytes = Encoding.ASCII.GetBytes(input);

            return new MemoryStream(bytes);
        }

        protected async Task<string> ToStringAsync(Stream? input)
        {
            #pragma warning disable CS8604
            using (var reader = new StreamReader(input))
            #pragma warning restore CS8604
            {
                return await reader.ReadToEndAsync();
            }
        }

        protected void SetupPutResult(StoragePutResult result)
        {
            _storage
                .Setup(
                    s => s.PutAsync(
                        It.IsAny<string>(),
                        It.IsAny<Stream>(),
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()))
                .Callback((string path, Stream content, string contentType, CancellationToken cancellationToken) =>
                {
                    _puts[path] = (content, contentType);
                })
                .ReturnsAsync(result);
        }
    }
}