using BaGet.Core;

namespace BaGet.Azure
{
    /// <summary>
    /// Allows updating the <see cref="Package.Downloads"/> column.
    /// </summary>
    public partial class TablePackageService
    {
        internal interface IDownloadCount
        {
            long Downloads { get; set; }
        }
    }
}
