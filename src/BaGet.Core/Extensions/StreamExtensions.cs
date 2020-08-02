using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    public static class StreamExtensions
    {
        // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
        private const int DefaultCopyBufferSize = 81920;

        /// <summary>
        /// Copies a stream to a file, and returns that file as a stream. The underlying file will be
        /// deleted when the resulting stream is disposed. The input stream will be disposed.
        /// </summary>
        /// <param name="original">
        /// The stream to be copied at its current position. The stream will be disposed.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The copied stream, with its position reset to the beginning.</returns>
        public static async Task<FileStream> AsTemporaryFileStreamAsync(
            this Stream original,
            CancellationToken cancellationToken)
        {
            FileStream result = null;

            try
            {
                result = new FileStream(
                    Path.GetTempFileName(),
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    DefaultCopyBufferSize,
                    FileOptions.DeleteOnClose);

                await original.CopyToAsync(result, DefaultCopyBufferSize, cancellationToken);
                result.Position = 0;
            }
            catch (Exception)
            {
                result?.Dispose();
                throw;
            }
            finally
            {
                original.Dispose();
            }

            return result;
        }

        public static bool Matches(this Stream content, Stream target)
        {
            using (var sha256 = SHA256.Create())
            {
                var contentHash = sha256.ComputeHash(content);
                var targetHash = sha256.ComputeHash(target);

                return contentHash.SequenceEqual(targetHash);
            }
        }
    }
}
