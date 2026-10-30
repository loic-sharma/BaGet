using Microsoft.AspNetCore.Http;
using BaGet.Web.Application.Models.Upload;

namespace BaGet.Web.V3.Extensions;

/// <summary>Adapts the files of a multipart request to the HTTP-free <see cref="UploadedFile"/> that a use case takes.</summary>
internal static class FormFileExtensions
{
    /// <summary>The file as a use case sees it, or null for no file.</summary>
    public static UploadedFile? ToUploadedFile(this IFormFile? file)
        => file == null ? null : new UploadedFile(file.FileName, file.Length, file.OpenReadStream);
}
