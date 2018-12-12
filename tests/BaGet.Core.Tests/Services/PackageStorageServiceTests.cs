using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class PackageStorageServiceTests
    {
        public class SavePackageContentAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfPackageIsNull()
            {
                await Assert.ThrowsAsync<ArgumentNullException>(
                    () => _target.SavePackageContentAsync(
                        null,
                        packageStream: Stream.Null,
                        nuspecStream: Stream.Null,
                        readmeStream: Stream.Null));
            }

            [Fact]
            public async Task ThrowsIfPackageStreamIsNull()
            {
                await Assert.ThrowsAsync<ArgumentNullException>(
                    () => _target.SavePackageContentAsync(
                        _package,
                        packageStream: null,
                        nuspecStream: Stream.Null,
                        readmeStream: Stream.Null));
            }

            [Fact]
            public async Task ThrowsIfNuspecStreamIsNull()
            {
                await Assert.ThrowsAsync<ArgumentNullException>(
                    () => _target.SavePackageContentAsync(
                        _package,
                        packageStream: Stream.Null,
                        nuspecStream: null,
                        readmeStream: Stream.Null));
            }

            [Fact]
            public async Task SavesContent()
            {
                // Arrange
                SetupPutResult(PutResult.Success);

                using (var packageStream = StringStream("My package"))
                using (var nuspecStream = StringStream("My nuspec"))
                using (var readmeStream = StringStream("My readme"))
                {
                    // Act
                    await _target.SavePackageContentAsync(
                        _package,
                        packageStream: packageStream,
                        nuspecStream: nuspecStream,
                        readmeStream: readmeStream);

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
                }
            }

            [Fact]
            public async Task DoesNotSaveReadmeIfItIsNull()
            {
                // Arrange
                SetupPutResult(PutResult.Success);

                using (var packageStream = StringStream("My package"))
                using (var nuspecStream = StringStream("My nuspec"))
                {
                    // Act
                    await _target.SavePackageContentAsync(
                        _package,
                        packageStream: packageStream,
                        nuspecStream: nuspecStream,
                        readmeStream: null);
                }

                // Assert
                Assert.False(_puts.ContainsKey(ReadmePath));
            }

            [Fact]
            public async Task NormalizesVersionWhenContentIsSaved()
            {
                // Arrange
                SetupPutResult(PutResult.Success);

                _package.Version = "1.2.3";
                using (var packageStream = StringStream("My package"))
                using (var nuspecStream = StringStream("My nuspec"))
                using (var readmeStream = StringStream("My readme"))
                {
                    // Act
                    await _target.SavePackageContentAsync(
                        _package,
                        packageStream: packageStream,
                        nuspecStream: nuspecStream,
                        readmeStream: readmeStream);
                }

                // Assert
                Assert.True(_puts.ContainsKey(PackagePath));
                Assert.True(_puts.ContainsKey(NuspecPath));
                Assert.True(_puts.ContainsKey(ReadmePath));
            }

            [Fact(Skip = "This behavior isn't implemented yet")]
            public async Task DoesNotThrowIfContentAlreadyExistsAndContentsMatch()
            {
                // Arrange
                SetupPutResult(PutResult.AlreadyExists);

                using (var packageStream = StringStream("My package"))
                using (var nuspecStream = StringStream("My nuspec"))
                using (var readmeStream = StringStream("My readme"))
                {
                    await _target.SavePackageContentAsync(
                        _package,
                        packageStream: packageStream,
                        nuspecStream: nuspecStream,
                        readmeStream: readmeStream);

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
                }
            }

            [Fact]
            public async Task ThrowsIfContentAlreadyExistsButContentsDoNotMatch()
            {
                // Arrange
                SetupPutResult(PutResult.Conflict);

                using (var packageStream = StringStream("My package"))
                using (var nuspecStream = StringStream("My nuspec"))
                using (var readmeStream = StringStream("My readme"))
                {
                    // Act
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        _target.SavePackageContentAsync(
                            _package,
                            packageStream: packageStream,
                            nuspecStream: nuspecStream,
                            readmeStream: readmeStream));
                }
            }
        }

        public class GetPackageStreamAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfStorageThrows()
            {
                // Arrange
                var cancellationToken = CancellationToken.None;
                _storage
                    .Setup(s => s.GetAsync(PackagePath, cancellationToken))
                    .ThrowsAsync(new DirectoryNotFoundException());

                // Act
                await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                    _target.GetPackageStreamAsync(_package.PackageId, NuGetVersion.Parse(_package.Version), cancellationToken));
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
                    var result = await _target.GetPackageStreamAsync(_package.PackageId, NuGetVersion.Parse(_package.Version), cancellationToken);

                    // Assert
                    Assert.Equal("My package", await ToStringAsync(result));

                    _storage.Verify(s => s.GetAsync(PackagePath, cancellationToken), Times.Once);
                }
            }
        }

        public class GetNuspecStreamAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfDoesntExist()
            {
                // Arrange
                var cancellationToken = CancellationToken.None;
                _storage
                    .Setup(s => s.GetAsync(NuspecPath, cancellationToken))
                    .ThrowsAsync(new DirectoryNotFoundException());

                // Act
                await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                    _target.GetNuspecStreamAsync(_package.PackageId, NuGetVersion.Parse(_package.Version), cancellationToken));
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
                    var result = await _target.GetNuspecStreamAsync(_package.PackageId, NuGetVersion.Parse(_package.Version), cancellationToken);

                    // Assert
                    Assert.Equal("My nuspec", await ToStringAsync(result));

                    _storage.Verify(s => s.GetAsync(NuspecPath, cancellationToken), Times.Once);
                }
            }
        }

        public class GetReadmeStreamAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfDoesntExist()
            {
                // Arrange
                var cancellationToken = CancellationToken.None;
                _storage
                    .Setup(s => s.GetAsync(ReadmePath, cancellationToken))
                    .ThrowsAsync(new DirectoryNotFoundException());

                // Act
                await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                    _target.GetReadmeStreamAsync(_package.PackageId, NuGetVersion.Parse(_package.Version), cancellationToken));
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
                    var result = await _target.GetReadmeStreamAsync(_package.PackageId, NuGetVersion.Parse(_package.Version), cancellationToken);

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
                await _target.DeleteAsync(_package.PackageId, NuGetVersion.Parse(_package.Version), cancellationToken);

                _storage.Verify(s => s.DeleteAsync(PackagePath, cancellationToken), Times.Once);
                _storage.Verify(s => s.DeleteAsync(NuspecPath, cancellationToken), Times.Once);
                _storage.Verify(s => s.DeleteAsync(ReadmePath, cancellationToken), Times.Once);
            }
        }

        public class FactsBase
        {
            protected readonly Package _package = new Package
            {
                PackageId = "My.Package",
                Version = "1.2.3"
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

            protected Stream StringStream(string input)
            {
                var bytes = Encoding.ASCII.GetBytes(input);

                return new MemoryStream(bytes);
            }

            protected async Task<string> ToStringAsync(Stream input)
            {
                using (var reader = new StreamReader(input))
                {
                    return await reader.ReadToEndAsync();
                }
            }

            protected void SetupPutResult(PutResult result)
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
}
