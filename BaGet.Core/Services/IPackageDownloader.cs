using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core.Services
{
    public interface IPackageDownloader
    {
        Task<Stream> DownloadAsync(Uri packageUri, CancellationToken cancellationToken);
    }
}
