using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>The request body of <c>PUT /{name}</c> exceeds <see cref="NpmOptions.MaxPublishBytes"/>.</summary>
public sealed class NpmBodyTooLargeException : Exception
{
    /// <param name="limit">The limit in bytes.</param>
    public NpmBodyTooLargeException(long limit) : base($"The request is larger than {limit} bytes.")
    {
        Limit = limit;
    }

    /// <summary>The limit in bytes.</summary>
    public long Limit { get; }
}

/// <summary>One tarball carried inline in a publish body, decoded to a temporary file.</summary>
public sealed class NpmAttachment : IDisposable
{
    internal NpmAttachment(string name, string? contentType, long? declaredLength, string path)
    {
        Name = name;
        ContentType = contentType;
        DeclaredLength = declaredLength;
        Path = path;
    }

    /// <summary>The key in <c>_attachments</c> (<c>name-1.0.0.tgz</c>, or <c>@scope/name-1.0.0.tgz</c>).</summary>
    public string Name { get; }

    /// <summary>The <c>content_type</c> the client declared.</summary>
    public string? ContentType { get; }

    /// <summary>The <c>length</c> the client declared, when it did.</summary>
    public long? DeclaredLength { get; }

    /// <summary>The temporary file holding the decoded bytes.</summary>
    public string Path { get; }

    /// <summary>The decoded size.</summary>
    public long Length { get; internal set; }

    /// <summary>Opens the decoded bytes for reading.</summary>
    public FileStream Open() => new(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// A parsed <c>npm publish</c> body: the JSON without the tarballs, and the tarballs decoded to temporary files.
/// </summary>
public sealed class NpmPublishDocument : IDisposable
{
    internal NpmPublishDocument(JsonObject root, IReadOnlyList<NpmAttachment> attachments)
    {
        Root = root;
        Attachments = attachments;
    }

    /// <summary>The document with every attachment's <c>data</c> emptied.</summary>
    public JsonObject Root { get; }

    /// <summary>The tarballs, in the order they appear.</summary>
    public IReadOnlyList<NpmAttachment> Attachments { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var attachment in Attachments) attachment.Dispose();
    }
}

/// <summary>
/// Reads the body of <c>PUT /{name}</c> without holding the tarball in memory. <c>npm publish</c> sends one JSON document
/// whose <c>_attachments</c> carry the tarball as a base64 string, so a 100 MB tarball is a 133 MB string. The body is
/// first copied to a temporary file (bounded by the limit), then one pass over it copies every byte except the base64 of
/// an attachment's <c>data</c> into a small buffer and notes where that string is; each attachment is then decoded straight
/// from the file into its own temporary file. Memory use is the size of the metadata (the manifest and readme), never of
/// the tarball.
/// </summary>
public static class NpmPublishBody
{
    private const int ChunkSize = 1 << 20;

    /// <summary>
    /// The most the document may hold apart from the tarballs (the manifest, readme and the rest), which is parsed in memory:
    /// a body that is nearly all JSON text and no tarball would otherwise be held whole.
    /// </summary>
    public const long MaxMetadataBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Reads the body.
    /// </summary>
    /// <param name="body">The request body.</param>
    /// <param name="maxBytes">The largest body accepted; zero or negative for no limit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="NpmBodyTooLargeException">The body is larger than <paramref name="maxBytes"/>.</exception>
    /// <exception cref="InvalidDataException">The body is not the JSON document of a publish, or an attachment is not base64.</exception>
    public static async Task<NpmPublishDocument> ParseAsync(Stream body, long maxBytes, CancellationToken cancellationToken)
    {
        if (body == null) throw new ArgumentNullException(nameof(body));

        var raw = TempFile();
        var attachments = new List<NpmAttachment>();
        try
        {
            await CopyBoundedAsync(body, raw, maxBytes, cancellationToken).ConfigureAwait(false);

            var (stripped, ranges) = await StripAsync(raw, cancellationToken).ConfigureAwait(false);
            JsonObject root;
            try
            {
                root = JsonNode.Parse(stripped) as JsonObject ?? throw new InvalidDataException("The request is not a JSON object.");
            }
            catch (JsonException e)
            {
                throw new InvalidDataException("The request is not valid JSON: " + e.Message, e);
            }

            foreach (var range in ranges)
            {
                var attachmentNode = (root["_attachments"] as JsonObject)?[range.Name] as JsonObject;
                var contentType = attachmentNode?["content_type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var typeText) ? typeText : null;
                long? declared = null;
                if (attachmentNode?["length"] is JsonValue lengthValue && lengthValue.TryGetValue<long>(out var l)) declared = l;

                var path = TempFile();
                var attachment = new NpmAttachment(range.Name, contentType, declared, path);
                attachments.Add(attachment);
                attachment.Length = await DecodeAsync(raw, range.Start, range.End, path, cancellationToken).ConfigureAwait(false);
            }

            return new NpmPublishDocument(root, attachments);
        }
        catch
        {
            foreach (var attachment in attachments) attachment.Dispose();
            throw;
        }
        finally
        {
            try
            {
                File.Delete(raw);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "baget-npm-" + Guid.NewGuid().ToString("N") + ".tmp");

    private static async Task CopyBoundedAsync(Stream body, string path, long maxBytes, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (maxBytes > 0 && total > maxBytes) throw new NpmBodyTooLargeException(maxBytes);

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private readonly record struct DataRange(string Name, long Start, long End);

    private struct Container
    {
        public bool IsObject;
        public bool ExpectKey;
        public string? Key;
    }

    /// <summary>
    /// One lexical pass: returns the document with the content of every <c>_attachments.{name}.data</c> string removed, and
    /// the file range of that content. Tracks strings, escapes and nesting only; it does not validate the JSON (parsing the
    /// stripped copy does).
    /// </summary>
    private static async Task<(byte[] Stripped, List<DataRange> Ranges)> StripAsync(string path, CancellationToken cancellationToken)
    {
        var output = new MemoryStream();
        var ranges = new List<DataRange>();
        var stack = new List<Container>();

        var inString = false;
        var escaped = false;
        var stringIsKey = false;
        var stringIsData = false;
        var keyBytes = new List<byte>();
        long dataStart = 0;
        string dataName = string.Empty;

        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[ChunkSize];
        long position = 0;
        int read;

        while ((read = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            var chunkStart = 0;
            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];

                if (inString && stringIsData)
                {
                    // The base64 text (possibly with escapes, undone when decoding): leave at the closing quote; everything before it is skipped.
                    var end = buffer.AsSpan(i, read - i).IndexOf((byte)'"');

                    if (end < 0)
                    {
                        i = read - 1;
                        chunkStart = read;
                        continue;
                    }

                    i += end;
                    ranges.Add(new DataRange(dataName, dataStart, position + i));
                    inString = false;
                    stringIsData = false;
                    output.WriteByte((byte)'"');
                    chunkStart = i + 1;
                    continue;
                }

                if (inString)
                {
                    if (stringIsKey && keyBytes.Count < 4096) keyBytes.Add(b);

                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (b == (byte)'\\')
                    {
                        escaped = true;
                    }
                    else if (b == (byte)'"')
                    {
                        inString = false;
                        if (stringIsKey)
                        {
                            // Drop the closing quote that was collected as part of the key bytes.
                            var key = DecodeKey(keyBytes);
                            var top = stack[^1];
                            top.Key = key;
                            stack[^1] = top;
                        }
                    }

                    continue;
                }

                switch (b)
                {
                    case (byte)'"':
                        inString = true;
                        escaped = false;
                        keyBytes.Clear();
                        stringIsKey = stack.Count > 0 && stack[^1].IsObject && stack[^1].ExpectKey;
                        stringIsData = !stringIsKey
                            && stack.Count == 3
                            && stack[0].Key == "_attachments"
                            && stack[2].Key == "data";
                        if (stringIsData)
                        {
                            dataName = stack[1].Key ?? string.Empty;
                            // Flush what precedes, including this opening quote; the content that follows is skipped.
                            output.Write(buffer, chunkStart, i + 1 - chunkStart);
                            chunkStart = i + 1;
                            dataStart = position + i + 1;
                        }

                        break;

                    case (byte)'{':
                        Open(stack, isObject: true);
                        break;

                    case (byte)'[':
                        Open(stack, isObject: false);
                        break;

                    case (byte)'}':
                    case (byte)']':
                        if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                        break;

                    case (byte)':':
                        if (stack.Count > 0 && stack[^1].IsObject)
                        {
                            var top = stack[^1];
                            top.ExpectKey = false;
                            stack[^1] = top;
                        }

                        break;

                    case (byte)',':
                        if (stack.Count > 0 && stack[^1].IsObject)
                        {
                            var top = stack[^1];
                            top.ExpectKey = true;
                            top.Key = null;
                            stack[^1] = top;
                        }

                        break;
                }
            }

            if (chunkStart < read && !(inString && stringIsData))
            {
                output.Write(buffer, chunkStart, read - chunkStart);
            }

            position += read;
            if (output.Length > MaxMetadataBytes) throw new NpmBodyTooLargeException(MaxMetadataBytes);
        }

        if (inString) throw new InvalidDataException("The request ends inside a string.");

        return (output.ToArray(), ranges);
    }

    // The key as the document means it: a JSON encoder may have escaped characters of a name (the + of a build version).
    private static string DecodeKey(List<byte> collected)
    {
        var raw = System.Text.Encoding.UTF8.GetString(collected.ToArray(), 0, Math.Max(collected.Count - 1, 0));
        if (raw.IndexOf('\\') < 0) return raw;

        try
        {
            return JsonSerializer.Deserialize<string>("\"" + raw + "\"") ?? raw;
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static void Open(List<Container> stack, bool isObject)
    {
        stack.Add(new Container { IsObject = isObject, ExpectKey = isObject });
    }

    private static async Task<long> DecodeAsync(string source, long start, long end, string target, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        input.Seek(start, SeekOrigin.Begin);

        var decoder = new JsonBase64Decoder(output);
        var remaining = end - start;
        var buffer = new byte[ChunkSize];

        while (remaining > 0)
        {
            var want = (int)Math.Min(buffer.Length, remaining);
            var filled = 0;
            while (filled < want)
            {
                var got = await input.ReadAsync(buffer.AsMemory(filled, want - filled), cancellationToken).ConfigureAwait(false);
                if (got == 0) throw new InvalidDataException("The request ended inside an attachment.");

                filled += got;
            }

            remaining -= want;
            decoder.Feed(buffer.AsSpan(0, want));
        }

        decoder.Finish();

        return decoder.Total;
    }

    /// <summary>
    /// Decodes the content of a JSON string that holds base64. JSON encoders differ in what they escape: JavaScript writes the
    /// text as it is, but others escape the slash (backslash, slash) or write the plus sign as a Unicode escape, so the escapes are undone before the
    /// base64 is decoded (whitespace escapes are skipped, as MIME-wrapped base64 may contain them). State survives chunk
    /// boundaries.
    /// </summary>
    private sealed class JsonBase64Decoder
    {
        private readonly Stream _output;
        private readonly byte[] _chars = new byte[1 << 16];
        private readonly byte[] _decoded = new byte[(1 << 16) / 4 * 3 + 3];
        private int _count;
        private int _state;
        private int _digits;
        private int _value;

        public JsonBase64Decoder(Stream output)
        {
            _output = output;
        }

        public long Total { get; private set; }

        public void Feed(ReadOnlySpan<byte> input)
        {
            foreach (var b in input)
            {
                switch (_state)
                {
                    case 0:
                        if (b == (byte)'\\') _state = 1; else Emit(b);
                        break;

                    case 1:
                        _state = 0;
                        if (b == (byte)'/' || b == (byte)'\\' || b == (byte)'"') Emit(b);
                        else if (b == (byte)'n' || b == (byte)'r' || b == (byte)'t') { }
                        else if (b == (byte)'u') { _state = 2; _digits = 0; _value = 0; }
                        else throw new InvalidDataException("An attachment's data holds an escape sequence that base64 does not.");
                        break;

                    default:
                        _value = (_value << 4) | HexValue(b);
                        if (++_digits == 4)
                        {
                            _state = 0;
                            if (_value > 0x7f) throw new InvalidDataException("An attachment's data holds a character that base64 does not.");

                            Emit((byte)_value);
                        }

                        break;
                }
            }
        }

        public void Finish()
        {
            if (_state != 0) throw new InvalidDataException("An attachment's data ends inside an escape sequence.");

            Flush(final: true);
        }

        private static int HexValue(byte b) => b switch
        {
            >= (byte)'0' and <= (byte)'9' => b - '0',
            >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
            >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
            _ => throw new InvalidDataException("An attachment's data holds an invalid escape sequence."),
        };

        private void Emit(byte b)
        {
            _chars[_count++] = b;
            if (_count == _chars.Length) Flush(final: false);
        }

        private void Flush(bool final)
        {
            if (_count == 0) return;

            var status = System.Buffers.Text.Base64.DecodeFromUtf8(_chars.AsSpan(0, _count), _decoded, out var consumed, out var written, isFinalBlock: final);
            if (status != System.Buffers.OperationStatus.Done || consumed != _count)
            {
                throw new InvalidDataException("An attachment's data is not valid base64.");
            }

            _output.Write(_decoded, 0, written);
            Total += written;
            _count = 0;
        }
    }
}
