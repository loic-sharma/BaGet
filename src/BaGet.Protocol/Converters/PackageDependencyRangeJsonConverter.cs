using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// This is an internal API that may be changed or removed without notice in any release.
    /// </summary>
    public class PackageDependencyRangeJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            // There are some quirky packages with arrays of dependency version ranges.
            // In this case, we take the first element.
            // Example: https://api.nuget.org/v3/catalog0/data/2016.02.21.11.06.01/dingu.generic.repo.ef7.1.0.0-beta2.json
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException();
            }

            reader.Read();
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException();
            }

            var result = reader.GetString();

            // Ignore all other strings until we reach the end of the array.
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException();
                }
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
