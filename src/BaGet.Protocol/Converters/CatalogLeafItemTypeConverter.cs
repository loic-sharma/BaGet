using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Protocol.Models;
using Newtonsoft.Json;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Serialization/CatalogLeafItemTypeConverter.cs
    /// </summary>
    internal class CatalogLeafItemTypeConverter : BaseCatalogLeafConverter
    {
        private static readonly Dictionary<CatalogLeafType, string> FromType = new Dictionary<CatalogLeafType, string>
        {
            { CatalogLeafType.PackageDelete, "nuget:PackageDelete" },
            { CatalogLeafType.PackageDetails, "nuget:PackageDetails" },
        };

        private static readonly Dictionary<string, CatalogLeafType> FromString = FromType
            .ToDictionary(x => x.Value, x => x.Key);

        public CatalogLeafItemTypeConverter() : base(FromType)
        {
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var stringValue = reader.Value as string;
            if (stringValue != null)
            {
                CatalogLeafType output;
                if (FromString.TryGetValue(stringValue, out output))
                {
                    return output;
                }
            }

            throw new JsonSerializationException($"Unexpected value for a {nameof(CatalogLeafType)}.");
        }
    }
}
