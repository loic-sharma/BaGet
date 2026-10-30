using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Cuts a forward-only stream into parts of a fixed size without holding more than one part: multipart APIs (S3, OSS, Azure
/// blocks, GCS resumable) want parts of a minimum size, while a chunk of a chunked upload may be any size.
/// </summary>
public static class StreamPartitioner
{
    /// <summary>
    /// Reads <paramref name="source"/> to its end, handing every full <paramref name="partSize"/> bytes to
    /// <paramref name="onPart"/> (the memory is only valid during the call), and returns the remainder (shorter than a part;
    /// possibly empty) that the caller keeps for the next chunk or sends as the last part.
    /// </summary>
    /// <param name="source">The bytes to cut.</param>
    /// <param name="carry">Bytes that come before <paramref name="source"/> (the previous chunk's remainder); shorter than a part.</param>
    /// <param name="partSize">The part size; positive.</param>
    /// <param name="onPart">Stores one part.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    public static async Task<byte[]> SplitAsync(
        Stream source,
        ReadOnlyMemory<byte> carry,
        int partSize,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> onPart,
        CancellationToken cancellationToken)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (onPart == null) throw new ArgumentNullException(nameof(onPart));
        if (partSize <= 0) throw new ArgumentOutOfRangeException(nameof(partSize));
        if (carry.Length >= partSize) throw new ArgumentException("The carried bytes are a whole part or more.", nameof(carry));

        var buffer = ArrayPool<byte>.Shared.Rent(partSize);
        try
        {
            carry.CopyTo(buffer);
            var filled = carry.Length;

            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(filled, partSize - filled), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;

                filled += read;
                if (filled == partSize)
                {
                    await onPart(buffer.AsMemory(0, partSize), cancellationToken).ConfigureAwait(false);
                    filled = 0;
                }
            }

            return buffer.AsSpan(0, filled).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
