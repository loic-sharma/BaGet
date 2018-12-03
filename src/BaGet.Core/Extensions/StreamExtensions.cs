using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core.Extensions
{
    public static class StreamExtensions
    {
        // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
        private const int DefaultCopyBufferSize = 81920;

        /// <summary>
        /// Copies a stream to a file, and returns that file as a stream. The underlying file will be
        /// deleted when the resulting stream is disposed.
        /// </summary>
        /// <param name="original">The stream to be copied, at its current position.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The copied stream, with its position reset to the beginning.</returns>
        public static async Task<FileStream> AsTemporaryFileStreamAsync(
            this Stream original,
            CancellationToken cancellationToken = default)
        {
            var result = new FileStream(
                Path.GetTempFileName(),
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                DefaultCopyBufferSize,
                FileOptions.DeleteOnClose);

            try
            {
                await original.CopyToAsync(result, DefaultCopyBufferSize, cancellationToken);
                result.Position = 0;
            }
            catch (Exception)
            {
                result.Dispose();
                throw;
            }

            return result;
        }
    }
}
