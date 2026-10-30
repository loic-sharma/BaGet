using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Core.Tests.Extensions;

public class StreamExtensionsTests
{
    private static MemoryStream Stream(string content) => new(Encoding.UTF8.GetBytes(content));

    public class Matches
    {
        [Fact]
        public void IsTrueForIdenticalContent()
        {
            using var a = Stream("hello world");
            using var b = Stream("hello world");

            Assert.True(a.Matches(b));
        }

        [Fact]
        public void IsFalseForDifferentContent()
        {
            using var a = Stream("hello world");
            using var b = Stream("hello mars!");

            Assert.False(a.Matches(b));
        }

        [Fact]
        public void IsFalseForDifferentLengths()
        {
            using var a = Stream("hello");
            using var b = Stream("hello world");

            Assert.False(a.Matches(b));
        }

        [Fact]
        public void IsTrueForTwoEmptyStreams()
        {
            using var a = Stream("");
            using var b = Stream("");

            Assert.True(a.Matches(b));
        }

        [Fact]
        public void IsFalseWhenOnlyOneStreamIsEmpty()
        {
            using var a = Stream("");
            using var b = Stream("content");

            Assert.False(a.Matches(b));
        }

        [Fact]
        public void ComparesFromTheCurrentPosition()
        {
            // FileStorageService.PutAsync rewinds the incoming stream before comparing,
            // because the hash is computed from wherever the stream currently sits.
            using var a = Stream("XXXhello");
            using var b = Stream("hello");

            a.Position = 3;

            Assert.True(a.Matches(b));
        }

        [Fact]
        public void ConsumesBothStreams()
        {
            // Documents why a caller cannot compare twice without rewinding.
            using var a = Stream("hello");
            using var b = Stream("hello");

            Assert.True(a.Matches(b));

            Assert.Equal(a.Length, a.Position);
            Assert.Equal(b.Length, b.Position);
        }
    }

    public class AsTemporaryFileStreamAsync
    {
        [Fact]
        public async Task CopiesTheContent()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using var original = Stream("the content");

            using var result = await original.AsTemporaryFileStreamAsync(cancellationToken);

            using var reader = new StreamReader(result, Encoding.UTF8, false, 1024, leaveOpen: true);
            Assert.Equal("the content", reader.ReadToEnd());
        }

        [Fact]
        public async Task ResetsThePositionToTheBeginning()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using var original = Stream("the content");

            using var result = await original.AsTemporaryFileStreamAsync(cancellationToken);

            Assert.Equal(0, result.Position);
        }

        [Fact]
        public async Task CopiesFromTheCurrentPosition()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using var original = Stream("SKIPcontent");
            original.Position = 4;

            using var result = await original.AsTemporaryFileStreamAsync(cancellationToken);

            using var reader = new StreamReader(result, Encoding.UTF8, false, 1024, leaveOpen: true);
            Assert.Equal("content", reader.ReadToEnd());
        }

        [Fact]
        public async Task HandlesAnEmptyStream()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using var original = Stream("");

            using var result = await original.AsTemporaryFileStreamAsync(cancellationToken);

            Assert.Equal(0, result.Length);
        }

        [Fact]
        public async Task DeletesTheFileWhenDisposed()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            string path;

            using (var original = Stream("the content"))
            using (var result = await original.AsTemporaryFileStreamAsync(cancellationToken))
            {
                path = result.Name;
                Assert.True(File.Exists(path));
            }

            Assert.False(File.Exists(path));
        }
    }
}