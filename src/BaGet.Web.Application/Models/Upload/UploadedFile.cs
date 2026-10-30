using System;
using System.IO;

namespace BaGet.Web.Application.Models.Upload;

/// <summary>
/// A file chosen in an upload form, as a use case sees it: the name the client gave, its size and a way to read it. The web layer adapts its
/// own file type to it (<c>IFormFile</c> of a multipart request), so a use case needs no HTTP types.
/// </summary>
public sealed class UploadedFile
{
    private readonly Func<Stream> _open;

    /// <summary>Creates the file.</summary>
    /// <param name="fileName">The name the client sent (never trusted as a path).</param>
    /// <param name="length">The size in bytes.</param>
    /// <param name="open">Opens the content for reading; each call returns a new stream the caller disposes.</param>
    public UploadedFile(string? fileName, long length, Func<Stream> open)
    {
        FileName = fileName ?? string.Empty;
        Length = length;
        _open = open ?? throw new ArgumentNullException(nameof(open));
    }

    /// <summary>The name the client sent.</summary>
    public string FileName { get; }

    /// <summary>The size in bytes.</summary>
    public long Length { get; }

    /// <summary>Opens the content for reading.</summary>
    public Stream OpenReadStream() => _open();
}
