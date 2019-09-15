using System;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// A catalog leaf. Represents a single package event.
    /// Leafs can be discovered from a <see cref="CatalogPage"/>.
    /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-leaf
    /// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Models/ICatalogLeafItem.cs
    /// </summary>
    public interface ICatalogLeafItem
    {
        DateTimeOffset CommitTimestamp { get; }
        string PackageId { get; }
        string PackageVersion { get; }
        CatalogLeafType Type { get; }
    }
}
