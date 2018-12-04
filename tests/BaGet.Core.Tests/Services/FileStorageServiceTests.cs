﻿using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Services;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class FileStorageServiceTests
    {
        public class GetAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfStorePathDoesNotExist()
            {
                await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                    _target.GetAsync("hello.txt"));
            }

            [Fact]
            public async Task ThrowsIfFileDoesNotExist()
            {
                // Ensure the store path exists.
                Directory.CreateDirectory(_storePath);

                await Assert.ThrowsAsync<FileNotFoundException>(() =>
                    _target.GetAsync("hello.txt"));

                await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                    _target.GetAsync("hello/world.txt"));
            }

            [Fact]
            public async Task GetsStream()
            {
                // Arrange
                using (var content = StringStream("Hello world"))
                {
                    await _target.PutAsync("hello.txt", content, "text/plain");
                }

                // Act
                var result = await _target.GetAsync("hello.txt");

                // Assert
                Assert.Equal("Hello world", await ToStringAsync(result));

            }
        }

        public class GetDownloadUriAsync : FactsBase
        {
            [Fact]
            public async Task CreatesUriEvenIfDoesntExist()
            {
                var result = await _target.GetDownloadUriAsync("test.txt");
                var expected = new Uri(Path.Combine(_storePath, "test.txt"));

                Assert.Equal(expected, result);
            }
        }

        public class PutAsync : FactsBase
        {
            [Fact]
            public async Task SavesContent()
            {
                PutResult result;
                using (var content = StringStream("Hello world"))
                {
                    result = await _target.PutAsync("test.txt", content, "text/plain");
                }

                var path = Path.Combine(_storePath, "test.txt");

                Assert.True(File.Exists(path));
                Assert.Equal(PutResult.Success, result);
                Assert.Equal("Hello world", await File.ReadAllTextAsync(path));
            }

            [Fact]
            public async Task ReturnsAlreadyExistsIfContentAlreadyExists()
            {
                // Arrange
                var path = Path.Combine(_storePath, "test.txt");

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "Hello world");

                PutResult result;
                using (var content = StringStream("Hello world"))
                {
                    // Act
                    result = await _target.PutAsync("test.txt", content, "text/plain");
                }

                // Assert
                Assert.Equal(PutResult.AlreadyExists, result);
            }

            [Fact]
            public async Task ReturnsConflictIfContentAlreadyExistsButContentsDoNotMatch()
            {
                // Arrange
                var path = Path.Combine(_storePath, "test.txt");

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "Hello world");

                PutResult result;
                using (var content = StringStream("foo bar"))
                {
                    // Act
                    result = await _target.PutAsync("test.txt", content, "text/plain");
                }

                // Assert
                Assert.Equal(PutResult.Conflict, result);
            }
        }

        public class DeleteAsync : FactsBase
        {
            [Fact]
            public async Task DoesNotThrowIfPathDoesNotExist()
            {
                await _target.DeleteAsync("test.txt");
            }

            [Fact]
            public async Task Deletes()
            {
                // Arrange
                var path = Path.Combine(_storePath, "test.txt");

                Directory.CreateDirectory(_storePath);
                await File.WriteAllTextAsync(path, "Hello world");

                // Act & Assert
                await _target.DeleteAsync("test.txt");

                Assert.False(File.Exists(path));
            }
        }

        public class FactsBase : IDisposable
        {
            protected readonly string _storePath;
            protected readonly FileStorageService _target;

            public FactsBase()
            {
                _storePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                _target = new FileStorageService(_storePath);
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
        }
    }
}
