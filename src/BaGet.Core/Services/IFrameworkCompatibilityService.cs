using System.Collections.Generic;

namespace BaGet.Core.Services
{
    public interface IFrameworkCompatibilityService
    {
        IReadOnlyList<string> FindAllCompatibleFrameworks(string framework);
    }
}
