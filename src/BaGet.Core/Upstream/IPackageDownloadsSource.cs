namespace BaGet.Core;

public interface IPackageDownloadsSource
{
    Task<Dictionary<string, Dictionary<string, long>>> GetPackageDownloadsAsync();
}