using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.SourceCode;

namespace BaGet.Core.Services
{
    public enum SourceCodeAddResult
    {
        PackageAlreadyExists,
        Success
    }

    public interface ISourceCodeService
    {
        Task<SourceCodeAddResult> IndexAsync(Package package, IEnumerable<SourceCodeAssembly> sourceCodes, CancellationToken cancellationToken);
    }
}
