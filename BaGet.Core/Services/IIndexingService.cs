using System.IO;
using System.Threading.Tasks;

namespace BaGet.Core.Services
{
    public enum IndexingResult
    {
        InvalidPackage,
        PackageAlreadyExists,
        Success,
    }

    public interface IIndexingService
    {
        Task<IndexingResult> IndexAsync(Stream stream);
    }
}