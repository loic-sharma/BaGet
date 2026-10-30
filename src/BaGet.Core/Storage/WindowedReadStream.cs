using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Reads a byte range of a remote blob as a sequence of bounded range requests (<see cref="DefaultWindow"/> each) instead of one
/// request for the whole range. A multi-gigabyte <c>GET</c> is one connection that must stay healthy for minutes and cannot resume;
/// here a connection that drops (or a server that closes a long response) costs one window, which is re-requested from the exact
/// byte already delivered. Memory: whatever the inner stream buffers, never the window.
/// </summary>
public sealed class WindowedReadStream : Stream
{
    /// <summary>The bytes asked for per request: 64 MiB.</summary>
    public const long DefaultWindow = 64L * 1024 * 1024;

    /// <summary>How often one window is re-requested after the connection fails before the error is passed on.</summary>
    public const int DefaultRetries = 3;

    private readonly Func<long, long, CancellationToken, Task<Stream>> _open;
    private readonly long _window;
    private readonly int _retries;
    private long _position;
    private readonly long _end;
    private Stream? _current;
    private long _windowEnd;
    private bool _disposed;

    /// <param name="open">Opens <c>count</c> bytes from <c>offset</c> (a stream that ends after them).</param>
    /// <param name="offset">Where the range starts.</param>
    /// <param name="count">How many bytes the range holds.</param>
    /// <param name="window">Bytes per request.</param>
    /// <param name="retries">Re-requests of a window after an I/O failure.</param>
    public WindowedReadStream(Func<long, long, CancellationToken, Task<Stream>> open, long offset, long count, long window = DefaultWindow, int retries = DefaultRetries)
    {
        _open = open ?? throw new ArgumentNullException(nameof(open));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (window <= 0) throw new ArgumentOutOfRangeException(nameof(window));
        _position = offset;
        _end = offset + count;
        _window = window;
        _retries = retries;
    }

    /// <summary>Requests made so far (windows opened, re-requests included), for tests.</summary>
    public int RequestCount { get; private set; }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowedReadStream));
        if (buffer.Length == 0) return 0;

        var failures = 0;
        while (_position < _end)
        {
            try
            {
                if (_current == null)
                {
                    _windowEnd = Math.Min(_end, _position + _window);
                    RequestCount++;
                    _current = await _open(_position, _windowEnd - _position, cancellationToken).ConfigureAwait(false);
                }

                var read = await _current.ReadAsync(buffer.Slice(0, (int)Math.Min(buffer.Length, _windowEnd - _position)), cancellationToken).ConfigureAwait(false);
                if (read > 0)
                {
                    _position += read;
                    if (_position >= _windowEnd) CloseWindow();
                    return read;
                }

                // The response ended before the window did: ask again from where it stopped.
                CloseWindow();
                if (++failures > _retries) throw new EndOfStreamException("The store ended a ranged read early.");
            }
            catch (Exception e) when ((e is IOException || e is HttpRequestException) && !cancellationToken.IsCancellationRequested && ++failures <= _retries)
            {
                CloseWindow();
            }
        }

        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void CloseWindow()
    {
        _current?.Dispose();
        _current = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            CloseWindow();
        }

        base.Dispose(disposing);
    }
}
