using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// Converts a single value or a list of values into the desired type or <see cref="List{T}"/>.
    /// </summary>
    /// <typeparam name="T">The desired type.</typeparam>
    internal class SingleOrListConverter<T> : JsonConverter
    {
        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(IReadOnlyList<T>);
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.StartArray)
            {
                return serializer.Deserialize<List<T>>(reader);
            }
            else
            {
                return serializer.Deserialize<T>(reader);
            }
        }

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }
}
