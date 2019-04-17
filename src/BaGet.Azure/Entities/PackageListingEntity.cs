using BaGet.Core;
using Microsoft.Azure.Cosmos.Table;

namespace BaGet.Azure
{
    /// <summary>
    /// The Azure Table Storage entity to update the <see cref="Package.Listed"/> column.
    /// The <see cref="TableEntity.PartitionKey"/> is the <see cref="Package.Id"/> and
    /// the <see cref="TableEntity.RowKey"/> is the <see cref="Package.Version"/>.
    /// </summary>
    public partial class TablePackageService
    {
        private class PackageListingEntity : TableEntity
        {
            public PackageListingEntity()
            {
            }

            public bool Listed { get; set; }
        }
    }
}
