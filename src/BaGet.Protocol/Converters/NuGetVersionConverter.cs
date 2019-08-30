using System;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// Converts a string into a <see cref="NuGetVersion"/>.
    /// </summary>
    public class NuGetVersionConverter : JsonConverter
    {
        private readonly NuGetVersionConversionFlags _flags;

        public NuGetVersionConverter()
            : this(NuGetVersionConversionFlags.Default)
        {
        }

        public NuGetVersionConverter(NuGetVersionConversionFlags flags)
        {
            _flags = flags;
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(NuGetVersion);
        }

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var version = (NuGetVersion)value;

            serializer.Serialize(writer, ToString(version, _flags));
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return reader.TokenType != JsonToken.Null ? NuGetVersion.Parse(serializer.Deserialize<string>(reader)) : null;
        }

        internal static string ToString(NuGetVersion version, NuGetVersionConversionFlags flags)
        {
            var versionString = ((flags & NuGetVersionConversionFlags.IncludeBuildMetadata) == 0)
                ? version.ToNormalizedString()
                : version.ToFullString();

            return ((flags & NuGetVersionConversionFlags.OriginalCasing) == 0)
                ? versionString.ToLowerInvariant()
                : versionString;
        }
    }
}
