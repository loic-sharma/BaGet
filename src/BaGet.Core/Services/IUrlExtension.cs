using NuGet.Versioning;
namespace BaGet.Core.Services
{
    // we need this interface to decouple BaGet.Core from AspNetCore.Mvc
    public interface IUrlExtension
    {
        string PackageRegistration(string id);
        string PackageRegistration(string id, NuGetVersion version);
        string PackageDownload(string id, NuGetVersion version);
    }
}
