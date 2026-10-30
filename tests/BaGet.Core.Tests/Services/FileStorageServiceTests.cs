using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BaGet.Core.Tests.Services;

public class FileStorageServiceTests
{
    public class GetAsync : FactsBase
    {
        [Fact]
        public async Task ThrowsIfStorePathDoesNotExist()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                _target.GetAsync("hello.txt", cancellationToken));
        }

        [Fact]
        public async Task ThrowsIfFileDoesNotExist()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // Ensure the store path exists.
            Directory.CreateDirectory(_storePath);

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                _target.GetAsync("hello.txt", cancellationToken));

            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                _target.GetAsync("hello/world.txt", cancellationToken));
        }

        [Fact]
        public async Task GetsStream()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // Arrange
            using (var content = StringStream("Hello world"))
            {
                await _target.PutAsync("hello.txt", content, "text/plain", cancellationToken);
            }

            // Act
            var result = await _target.GetAsync("hello.txt", cancellationToken);

            // Assert
            Assert.Equal("Hello world", await ToStringAsync(result));
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            #pragma warning disable CS8602
            foreach (var path in OutsideStorePathData)
            #pragma warning restore CS8602
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.GetAsync(path, cancellationToken));
            }
        }
    }

    public class GetDownloadUriAsync : FactsBase
    {
        [Fact]
        public async Task CreatesUriEvenIfDoesntExist()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var result = await _target.GetDownloadUriAsync("test.txt", cancellationToken);
            var expected = new Uri(Path.Combine(_storePath, "test.txt"));

            Assert.Equal(expected, result);
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            #pragma warning disable CS8602
            foreach (var path in OutsideStorePathData)
            #pragma warning restore CS8602
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.GetDownloadUriAsync(path, cancellationToken));
            }
        }
    }

    public class PutAsync : FactsBase
    {
        [Fact]
        public async Task SavesContent()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            StoragePutResult result;
            using (var content = StringStream("Hello world"))
            {
                result = await _target.PutAsync("test.txt", content, "text/plain", cancellationToken);
            }

            var path = Path.Combine(_storePath, "test.txt");

            Assert.True(File.Exists(path));
            Assert.Equal(StoragePutResult.Success, result);
            Assert.Equal("Hello world", await File.ReadAllTextAsync(path, cancellationToken));
        }

        [Fact]
        public async Task ReturnsAlreadyExistsIfContentAlreadyExists()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // Arrange
            var path = Path.Combine(_storePath, "test.txt");

            #pragma warning disable CS8604
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            #pragma warning restore CS8604
            File.WriteAllText(path, "Hello world");

            StoragePutResult result;
            using (var content = StringStream("Hello world"))
            {
                // Act
                result = await _target.PutAsync("test.txt", content, "text/plain", cancellationToken);
            }

            // Assert
            Assert.Equal(StoragePutResult.AlreadyExists, result);
        }

        [Fact]
        public async Task ReturnsConflictIfContentAlreadyExistsButContentsDoNotMatch()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // Arrange
            var path = Path.Combine(_storePath, "test.txt");

            #pragma warning disable CS8604
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            #pragma warning restore CS8604
            File.WriteAllText(path, "Hello world");

            StoragePutResult result;
            using (var content = StringStream("foo bar"))
            {
                // Act
                result = await _target.PutAsync("test.txt", content, "text/plain", cancellationToken);
            }

            // Assert
            Assert.Equal(StoragePutResult.Conflict, result);
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            #pragma warning disable CS8602
            foreach (var path in OutsideStorePathData)
            #pragma warning restore CS8602
            {
                using (var content = StringStream("Hello world"))
                {
                    await Assert.ThrowsAsync<ArgumentException>(async () =>
                        await _target.PutAsync(path, content, "text/plain", cancellationToken));
                }
            }
        }
    }

    public class DeleteAsync : FactsBase
    {
        [Fact]
        public async Task DoesNotThrowIfPathDoesNotExist()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await _target.DeleteAsync("test.txt", cancellationToken);
        }

        [Fact]
        public async Task Deletes()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            // Arrange
            var path = Path.Combine(_storePath, "test.txt");

            Directory.CreateDirectory(_storePath);
            await File.WriteAllTextAsync(path, "Hello world", cancellationToken);

            // Act & Assert
            await _target.DeleteAsync("test.txt", cancellationToken);

            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            #pragma warning disable CS8602
            foreach (var path in OutsideStorePathData)
            #pragma warning restore CS8602
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.DeleteAsync(path, cancellationToken));
            }
        }
    }

    public class ListAsync : FactsBase
    {
        [Fact]
        public async Task ListsEveryPathUnderThePrefix()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await _target.PutAsync("staging/a.json", StringStream("a"), "application/json", cancellationToken);
            await _target.PutAsync("staging/nested/b.json", StringStream("b"), "application/json", cancellationToken);
            await _target.PutAsync("packages/elsewhere.nupkg", StringStream("c"), "application/octet-stream", cancellationToken);

            var result = await _target.ListAsync("staging", cancellationToken);

            // Store relative and "/" separated, so each path can go straight back to
            // GetAsync or DeleteAsync on any platform.
            Assert.Equal(
                new[] { "staging/a.json", "staging/nested/b.json" },
                result.OrderBy(p => p, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public async Task IsEmptyWhenThePrefixHoldsNothing()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            Assert.Empty(await _target.ListAsync("staging", cancellationToken));

            await _target.PutAsync("packages/elsewhere.nupkg", StringStream("c"), "application/octet-stream", cancellationToken);

            Assert.Empty(await _target.ListAsync("staging", cancellationToken));
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            #pragma warning disable CS8602
            foreach (var path in OutsideStorePathData)
            #pragma warning restore CS8602
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.ListAsync(path, cancellationToken));
            }
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly string _storePath;
        protected readonly Mock<IOptionsSnapshot<FileSystemStorageOptions>> _options;
        protected readonly FileStorageService _target;

        public FactsBase()
        {
            _storePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            _options = new Mock<IOptionsSnapshot<FileSystemStorageOptions>>();

            _options
                .Setup(o => o.Value)
                .Returns(() => new FileSystemStorageOptions { Path = _storePath });

            _target = new FileStorageService(_options.Object);
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

        public IEnumerable<string>? OutsideStorePathData
        {
            get
            {
                var fullPath = Path.GetFullPath(_storePath);
                yield return "../file";
                yield return ".";
                yield return $"../{Path.GetFileName(_storePath)}";
                yield return $"../{Path.GetFileName(_storePath)}suffix";
                yield return $"../{Path.GetFileName(_storePath)}suffix/file";
                yield return fullPath;
                yield return fullPath + Path.DirectorySeparatorChar;
                yield return fullPath + Path.DirectorySeparatorChar + "..";
                yield return fullPath + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "file";
                #pragma warning disable CS8603
                yield return Path.GetPathRoot(_storePath);
                #pragma warning restore CS8603
                #pragma warning disable CS8604
                yield return Path.Combine(Path.GetPathRoot(_storePath), "file");
                #pragma warning restore CS8604
            }
        }
    }
}