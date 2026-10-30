using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Core.Tests.Formats;

/// <summary>Collection that runs alone: a test that counts the process's allocations cannot share it with tests running in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationMeasurements
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Allocation measurements";
}

/// <summary>The memory the publish body parser uses for a large tarball.</summary>
[Collection(AllocationMeasurements.Name)]
public class NpmPublishBodyMemoryTests
{
    private static byte[] Bytes(int length, int seed = 7)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public async Task ALargeTarballCrossesChunkBoundariesIntactAndIsNeverHeldInMemory()
    {
        var tarball = Bytes(24 * 1024 * 1024);
        var path = Path.GetTempFileName();
        try
        {
            // 32 MB of base64 text, written to a file the way a request body arrives, with metadata before and after.
            await File.WriteAllTextAsync(path, "{\"name\":\"left-pad\",\"readme\":\"" + new string('r', 100_000) + "\",\"_attachments\":{\"left-pad-1.0.0.tgz\":{\"content_type\":\"application/octet-stream\",\"data\":\"", TestContext.Current.CancellationToken);
            await using (var append = new FileStream(path, FileMode.Append))
            await using (var encoder = new CryptoStream(append, new ToBase64Transform(), CryptoStreamMode.Write, leaveOpen: true))
            {
                await encoder.WriteAsync(tarball, TestContext.Current.CancellationToken);
            }

            await File.AppendAllTextAsync(path, "\",\"length\":" + tarball.Length + "}},\"after\":\"" + new string('z', 100_000) + "\"}", TestContext.Current.CancellationToken);

            var before = GC.GetTotalAllocatedBytes(precise: true);
            await using var input = File.OpenRead(path);
            using var document = await NpmPublishBody.ParseAsync(input, 0, TestContext.Current.CancellationToken);
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

            var attachment = Assert.Single(document.Attachments);
            Assert.Equal(tarball.Length, attachment.Length);
            await using var decoded = attachment.Open();
            Assert.Equal(Convert.ToHexString(SHA256.HashData(tarball)), Convert.ToHexString(await SHA256.HashDataAsync(decoded, TestContext.Current.CancellationToken)));
            Assert.Equal(100_000, ((string)document.Root["after"]!).Length);
            Assert.Equal(100_000, ((string)document.Root["readme"]!).Length);

            // Working buffers are a few MB; a copy of the 32 MB text, or of the 24 MB tarball, would show here.
            Assert.True(allocated < 16L * 1024 * 1024, $"allocated {allocated} bytes parsing a 24 MB tarball");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ABodyThatIsAllMetadataIsRefusedOnceItOutgrowsTheMetadataLimit()
    {
        // 70 MB of readme and no tarball: this would be held in memory whole to be parsed.
        var text = "{\"name\":\"left-pad\",\"readme\":\"" + new string('x', 70 * 1024 * 1024) + "\"}";
        using var body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

        await Assert.ThrowsAsync<NpmBodyTooLargeException>(() => NpmPublishBody.ParseAsync(body, 0, TestContext.Current.CancellationToken));
    }
}
