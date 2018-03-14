using System.IO;
using System.Threading.Tasks;

namespace BaGet.Core.Indexing
{
    public enum Result
    {
        InvalidPackage,
        PackageAlreadyExists,
        Success,
    }

    public interface IIndexingService
    {
        Task<Result> IndexAsync(Stream stream);
    }
}