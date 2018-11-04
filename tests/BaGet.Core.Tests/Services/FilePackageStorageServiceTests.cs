using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Services;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class FilePackageStorageServiceTests
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
                Assert.Equal("My package", await File.ReadAllTextAsync(PackagePath));
                Assert.Equal("My nuspec", await File.ReadAllTextAsync(NuspecPath));
                Assert.Equal("My readme", await File.ReadAllTextAsync(ReadmePath));
            }

            [Fact]
            public async Task DoesNotSaveReadmeIfItIsNull()
            {
                // Arrange
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
                Assert.False(File.Exists(ReadmePath));
            }

            [Fact]
            public async Task NormalizesVersionWhenContentIsSaved()
            {
                // Arrange
                _package.Version = new NuGetVersion("1.2.3.0");
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
                Assert.Equal("My package", await File.ReadAllTextAsync(PackagePath));
                Assert.Equal("My nuspec", await File.ReadAllTextAsync(NuspecPath));
                Assert.Equal("My readme", await File.ReadAllTextAsync(ReadmePath));
            }

            [Fact(Skip = "This behavior isn't implemented yet")]
            public async Task DoesNotThrowIfContentAlreadyExistsAndContentsMatch()
            {
                // Arrange
                using (var packageStream = StringStream("My package"))
                using (var nuspecStream = StringStream("My nuspec"))
                using (var readmeStream = StringStream("My readme"))
                {
                    await _target.SavePackageContentAsync(
                        _package,
                        packageStream: packageStream,
                        nuspecStream: nuspecStream,
                        readmeStream: readmeStream);

                    Assert.Equal("My package", await File.ReadAllTextAsync(PackagePath));
                    Assert.Equal("My nuspec", await File.ReadAllTextAsync(NuspecPath));
                    Assert.Equal("My readme", await File.ReadAllTextAsync(ReadmePath));

                    // TODO - This should work without throwing!
                    // Act
                    await _target.SavePackageContentAsync(
                        _package,
                        packageStream: packageStream,
                        nuspecStream: nuspecStream,
                        readmeStream: readmeStream);
                }

                // Assert
                Assert.Equal("My package", await File.ReadAllTextAsync(PackagePath));
                Assert.Equal("My nuspec", await File.ReadAllTextAsync(NuspecPath));
                Assert.Equal("My readme", await File.ReadAllTextAsync(ReadmePath));
            }

            [Fact]
            public async Task ThrowsIfContentAlreadyExistsButContentsDoNotMatch()
            {
                // Arrange
                using (var packageStream = StringStream("My package #1"))
                using (var nuspecStream = StringStream("My nuspec #1"))
                using (var readmeStream = StringStream("My readme #1"))
                {
                    await _target.SavePackageContentAsync(
                        _package,
                        packageStream: packageStream,
                        nuspecStream: nuspecStream,
                        readmeStream: readmeStream);
                }

                Assert.Equal("My package #1", await File.ReadAllTextAsync(PackagePath));
                Assert.Equal("My nuspec #1", await File.ReadAllTextAsync(NuspecPath));
                Assert.Equal("My readme #1", await File.ReadAllTextAsync(ReadmePath));

                using (var packageStream = StringStream("My package #2"))
                using (var nuspecStream = StringStream("My nuspec #2"))
                using (var readmeStream = StringStream("My readme #2"))
                {
                    // Act
                    await Assert.ThrowsAsync<IOException>(() =>
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
            public async Task ThrowsIfDoesntExist()
            {
                Directory.CreateDirectory(_storePath);

                await Assert.ThrowsAsync<DirectoryNotFoundException>(() => 
                    _target.GetPackageStreamAsync(_package.Id, _package.Version));
            }

            [Fact]
            public async Task GetsStream()
            {
                // Arrange
                using (var packageStream = StringStream("My package"))
                {
                    await _target.SavePackageContentAsync(
                        _package,
                        packageStream: packageStream,
                        nuspecStream: Stream.Null,
                        readmeStream: null);
                }

                // Act
                var result = await _target.GetPackageStreamAsync(_package.Id, _package.Version);

                // Assert
                Assert.Equal("My package", await ToStringAsync(result));
            }
        }

        public class GetNuspecStreamAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfDoesntExist()
            {
                Directory.CreateDirectory(_storePath);

                await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                    _target.GetNuspecStreamAsync(_package.Id, _package.Version));
            }

            [Fact]
            public async Task GetsStream()
            {
                // Arrange
                using (var nuspecStream = StringStream("My nuspec"))
                {
                    await _target.SavePackageContentAsync(
                        _package,
                        packageStream: Stream.Null,
                        nuspecStream: nuspecStream,
                        readmeStream: null);
                }

                // Act
                var result = await _target.GetNuspecStreamAsync(_package.Id, _package.Version);

                // Assert
                Assert.Equal("My nuspec", await ToStringAsync(result));
            }
        }

        public class GetReadmeStreamAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfDoesntExist()
            {
                Directory.CreateDirectory(_storePath);

                await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                    _target.GetNuspecStreamAsync(_package.Id, _package.Version));
            }

            [Fact]
            public async Task GetsStream()
            {
                // Arrange
                using (var readmeStream = StringStream("My readme"))
                {
                    await _target.SavePackageContentAsync(
                        _package,
                        packageStream: Stream.Null,
                        nuspecStream: Stream.Null,
                        readmeStream: readmeStream);
                }

                // Act
                var result = await _target.GetReadmeStreamAsync(_package.Id, _package.Version);

                // Assert
                Assert.Equal("My readme", await ToStringAsync(result));
            }
        }

        public class DeleteAsync : FactsBase
        {
            [Fact]
            public async Task DoesNotThrowIfPackagePathDoesNotExist()
            {
                await _target.DeleteAsync(_package.Id, _package.Version);
            }

            [Theory]
            [InlineData(false, false, false)]
            [InlineData(false, false, true)]
            [InlineData(false, true, false)]
            [InlineData(false, true, true)]
            [InlineData(true, false, false)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(true, true, true)]
            public async Task DeletesAnyExistingContent(bool packageExists, bool nuspecExists, bool readmeExists)
            {
                // Arrange
                Directory.CreateDirectory(PackageDirectory);

                if (packageExists) await File.WriteAllTextAsync(PackagePath, "My package");
                if (nuspecExists) await File.WriteAllTextAsync(NuspecPath, "My nuspec");
                if (readmeExists) await File.WriteAllTextAsync(ReadmePath, "My readme");

                // Act
                await _target.DeleteAsync(_package.Id, _package.Version);

                // Assert
                Assert.False(File.Exists(PackagePath));
                Assert.False(File.Exists(NuspecPath));
                Assert.False(File.Exists(ReadmePath));
            }
        }

        public class FactsBase : IDisposable
        {
            protected readonly Package _package = new Package
            {
                Id = "My.Package",
                Version = new NuGetVersion("1.2.3")
            };

            protected readonly string _storePath;
            protected readonly FilePackageStorageService _target;

            public FactsBase()
            {
                _storePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                _target = new FilePackageStorageService(_storePath);
            }

            protected string PackageDirectory => Path.Combine(_storePath, "my.package", "1.2.3");
            protected string PackagePath => Path.Combine(_storePath, "my.package", "1.2.3", "my.package.1.2.3.nupkg");
            protected string NuspecPath => Path.Combine(_storePath, "my.package", "1.2.3", "my.package.nuspec");
            protected string ReadmePath => Path.Combine(_storePath, "my.package", "1.2.3", "readme");

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
        }
    }
}
