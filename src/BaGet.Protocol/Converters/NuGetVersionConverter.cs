using System;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol.Converters
{
    public class NuGetVersionConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(NuGetVersion);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var version = (NuGetVersion)value;
            serializer.Serialize(writer, version.ToFullString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return reader.TokenType != JsonToken.Null ? NuGetVersion.Parse(serializer.Deserialize<string>(reader)) : null;
        }
    }
}
