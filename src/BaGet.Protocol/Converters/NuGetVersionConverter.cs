using System;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    public class NuGetVersionConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value.ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return reader.TokenType != JsonToken.Null ? NuGetVersion.Parse(serializer.Deserialize<string>(reader)) : null;
        }

        public override bool CanConvert(Type objectType) => objectType == typeof(NuGetVersion);
    }
}
