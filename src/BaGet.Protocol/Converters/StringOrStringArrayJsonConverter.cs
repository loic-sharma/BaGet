using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// This is an internal API that may be changed or removed without notice in any release.
    /// </summary>
    public class StringOrStringArrayJsonConverter : JsonConverter<IReadOnlyList<string>>
    {
        public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Try to read a single string first.
            if (reader.TokenType == JsonTokenType.String)
            {
                return new List<string> { reader.GetString() };
            }

            // Try to read an array of strings.
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException();
            }

            var result = new List<string>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    result.Add(reader.GetString());
                }
                else if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return result;
                }
                else
                {
                    break;
                }
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> values, JsonSerializerOptions options)
        {
            writer.WriteStartArray();

            foreach (var value in values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }
    }
}
