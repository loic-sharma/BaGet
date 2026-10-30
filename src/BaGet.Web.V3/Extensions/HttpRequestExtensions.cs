using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Http;
using NuGet.Packaging;

namespace BaGet.Web.V3.Extensions;

public static class HttpRequestExtensions
{
    public const string ApiKeyHeader = "X-NuGet-ApiKey";

    public static async Task<Stream> GetUploadStreamOrNullAsync(this HttpRequest request, CancellationToken cancellationToken)
    {
        // Try to get the nupkg from the multipart/form-data. If that's empty,
        // fallback to the request's body.
        Stream? rawUploadStream = null;
        try
        {
            if (request.HasFormContentType && request.Form.Files.Count > 0)
            {
                rawUploadStream = request.Form.Files[0].OpenReadStream();
            }
            else
            {
                rawUploadStream = request.Body;
            }

            // Convert the upload stream into a temporary file stream to
            // minimize memory usage.
            #pragma warning disable CS8602
            return await rawUploadStream?.AsTemporaryFileStreamAsync(cancellationToken);
            #pragma warning restore CS8602
        }
        finally
        {
            rawUploadStream?.Dispose();
        }
    }

    /// <summary>
    /// Read the package ID from an uploaded package, or null if it cannot be read. The
    /// stream is left open and rewound.
    /// </summary>
    public static string? ReadPackageIdOrNull(this Stream package)
    {
        try
        {
            using (var reader = new PackageArchiveReader(package, leaveStreamOpen: true))
            {
                return reader.NuspecReader.GetId();
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            package.Position = 0;
        }
    }

    public static string? GetApiKey(this HttpRequest request)
    {
        return request.Headers[ApiKeyHeader];
    }
}