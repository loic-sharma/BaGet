using BaGet.Core;

namespace BaGet.Azure
{
    /// <summary>
    /// Allows updating the <see cref="Package.Listed"/> column.
    /// </summary>
    public partial class TablePackageService
    {
        internal interface IListed
        {
            bool Listed { get; set; }
        }
    }
}
