using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Reads the <c>package.json</c> of an npm tarball (a gzip-compressed tar whose files sit under one top folder, usually
/// <c>package/</c>). The registry uses it to check that the tarball a client sends is the package and version it claims
/// to be, and the browser upload builds the version's manifest from it.
/// </summary>
public static class NpmTarballReader
{
    // A tarball is untrusted: what it expands to is read only as far as package.json (usually its first entry), and never
    // further than this, so a compression bomb costs bounded time.
    private const long MaxExpandedBytes = 1L << 30;
    private const int MaxEntries = 100_000;
    private const long MaxManifestBytes = 4 * 1024 * 1024;

    /// <summary>
    /// The <c>package.json</c> object of the tarball, or null when the stream is not a gzip-compressed tar, holds no
    /// <c>package.json</c> at the root of its top folder, or that file is not a JSON object.
    /// </summary>
    /// <param name="tarball">A readable stream positioned at the start; it is not closed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<JsonObject?> ReadPackageJsonAsync(Stream tarball, CancellationToken cancellationToken)
    {
        if (tarball == null) throw new ArgumentNullException(nameof(tarball));

        try
        {
            using var gzip = new GZipStream(tarball, CompressionMode.Decompress, leaveOpen: true);
            using var limited = new LimitedStream(gzip, MaxExpandedBytes);
            using var reader = new TarReader(limited, leaveOpen: true);

            for (var count = 0; count < MaxEntries; count++)
            {
                var entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false);
                if (entry == null) return null;
                if (entry.EntryType != TarEntryType.RegularFile && entry.EntryType != TarEntryType.V7RegularFile) continue;
                if (!IsRootPackageJson(entry.Name)) continue;
                if (entry.DataStream == null || entry.Length > MaxManifestBytes) return null;

                using var buffer = new MemoryStream();
                await entry.DataStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                buffer.Position = 0;

                return JsonNode.Parse(buffer) as JsonObject;
            }

            return null;
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or FormatException or IOException or EndOfStreamException)
        {
            return null;
        }
    }

    // <top>/package.json: npm packs everything below one folder (conventionally "package"), whatever the package is called.
    private static bool IsRootPackageJson(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('.', '/');
        var slash = normalized.IndexOf('/');

        return slash > 0 && string.Equals(normalized.Substring(slash + 1), "package.json", StringComparison.Ordinal);
    }

    /// <summary>Reads at most a fixed number of bytes from the inner stream, then fails as a corrupt archive.</summary>
    private sealed class LimitedStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        public LimitedStream(Stream inner, long limit)
        {
            _inner = inner;
            _remaining = limit;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = _inner.Read(buffer.Slice(0, (int)Math.Min(buffer.Length, Math.Max(_remaining, 0) + 1)));
            Account(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer.Slice(0, (int)Math.Min(buffer.Length, Math.Max(_remaining, 0) + 1)), cancellationToken).ConfigureAwait(false);
            Account(read);
            return read;
        }

        private void Account(int read)
        {
            _remaining -= read;
            if (_remaining < 0) throw new InvalidDataException("The tarball expands to more than the registry reads.");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
