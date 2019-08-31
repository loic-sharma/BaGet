using System;

namespace BaGet.Protocol
{
    public interface ICatalogLeafItem
    {
        DateTimeOffset CommitTimestamp { get; }
        string PackageId { get; }
        string PackageVersion { get; }
        CatalogLeafType Type { get; }
    }
}
