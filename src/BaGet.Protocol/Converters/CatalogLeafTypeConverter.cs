using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace BaGet.Protocol.Internal
{
    internal class CatalogLeafTypeConverter : BaseCatalogLeafConverter
    {
        private static readonly Dictionary<CatalogLeafType, string> FromType = new Dictionary<CatalogLeafType, string>
        {
            { CatalogLeafType.PackageDelete, "PackageDelete" },
            { CatalogLeafType.PackageDetails, "PackageDetails" },
        };

        private static readonly Dictionary<string, CatalogLeafType> FromString = FromType
            .ToDictionary(x => x.Value, x => x.Key);

        public CatalogLeafTypeConverter() : base(FromType)
        {
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            List<object> types;
            if (reader.TokenType == JsonToken.StartArray)
            {
                types = serializer.Deserialize<List<object>>(reader);
            }
            else
            {
                types = new List<object> { reader.Value };
            }

            foreach (var type in types.OfType<string>())
            {
                CatalogLeafType output;
                if (FromString.TryGetValue(type, out output))
                {
                    return output;
                }
            }

            throw new JsonSerializationException($"Unexpected value for a {nameof(CatalogLeafType)}.");
        }
    }
}
