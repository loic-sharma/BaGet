using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core.Services
{
    // See: https://github.com/NuGet/NuGet.Jobs/blob/master/src/Validation.Common.Job/PackageDownloader.cs
    public class PackageDownloader : IPackageDownloader
    {
        private const int BufferSize = 8192;

        private readonly HttpClient _httpClient;
        private readonly ILogger<PackageDownloader> _logger;

        public PackageDownloader(HttpClient httpClient, ILogger<PackageDownloader> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Stream> DownloadAsync(Uri packageUri, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Attempting to download package from {PackageUri}...", packageUri);

            Stream packageStream = null;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Download the package from the network to a temporary file.
                using (var response = await _httpClient.GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead))
                {
                    _logger.LogInformation(
                        "Received response {StatusCode}: {ReasonPhrase} of type {ContentType} for request {PackageUri}",
                        response.StatusCode,
                        response.ReasonPhrase,
                        response.Content.Headers.ContentType,
                        packageUri);

                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new InvalidOperationException($"Expected status code {HttpStatusCode.OK} for package download, actual: {response.StatusCode}");
                    }

                    using (var networkStream = await response.Content.ReadAsStreamAsync())
                    {
                        packageStream = new FileStream(
                                            Path.GetTempFileName(),
                                            FileMode.Create,
                                            FileAccess.ReadWrite,
                                            FileShare.None,
                                            BufferSize,
                                            FileOptions.DeleteOnClose | FileOptions.Asynchronous);

                        await networkStream.CopyToAsync(packageStream, BufferSize, cancellationToken);
                    }
                }

                packageStream.Position = 0;

                _logger.LogInformation(
                    "Downloaded {PackageSizeInBytes} bytes in {DownloadElapsedTime} seconds for request {PackageUri}",
                    packageStream.Length,
                    stopwatch.Elapsed.TotalSeconds,
                    packageUri);

                return packageStream;
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "Exception thrown when trying to download package from {PackageUri}",
                    packageUri);

                packageStream?.Dispose();

                throw;
            }
        }
    }
}
