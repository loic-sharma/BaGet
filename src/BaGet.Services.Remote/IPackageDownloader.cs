using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Services.Remote
{
    public interface IPackageDownloader
    {
        Task<Stream> DownloadAsync(Uri packageUri, CancellationToken cancellationToken);
    }
}
