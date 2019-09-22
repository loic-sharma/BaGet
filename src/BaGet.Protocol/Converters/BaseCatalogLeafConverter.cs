using System;
using System.Collections.Generic;
using BaGet.Protocol.Models;
using Newtonsoft.Json;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Serialization/BaseCatalogLeafConverter.cs
    /// </summary>
    internal abstract class BaseCatalogLeafConverter : JsonConverter
    {
        private readonly IReadOnlyDictionary<CatalogLeafType, string> _fromType;

        public BaseCatalogLeafConverter(IReadOnlyDictionary<CatalogLeafType, string> fromType)
        {
            _fromType = fromType;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(CatalogLeafType);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (!_fromType.TryGetValue((CatalogLeafType)value, out var output))
            {
                throw new NotSupportedException($"The catalog leaf type '{value}' is not supported.");
            }

            writer.WriteValue(output);
        }
    }
}
