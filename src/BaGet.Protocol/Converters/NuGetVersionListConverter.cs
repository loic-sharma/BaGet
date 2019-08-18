using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// Converts a list of strings into a <see cref="IReadOnlyList{NuGetVersion}"/>.
    /// </summary>
    public class NuGetVersionListConverter : JsonConverter
    {
        private readonly NuGetVersionConversionFlags _flags;

        public NuGetVersionListConverter()
            : this(NuGetVersionConversionFlags.Default)
        {
        }

        public NuGetVersionListConverter(NuGetVersionConversionFlags flags)
        {
            _flags = flags;
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(IReadOnlyList<NuGetVersion>);
        }

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var versions = ((IReadOnlyList<NuGetVersion>)value);

            serializer.Serialize(writer, versions.Select(v => NuGetVersionConverter.ToString(v, _flags)));
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return serializer.Deserialize<IReadOnlyList<string>>(reader)
                .Select(NuGetVersion.Parse)
                .ToList();
        }
    }
}
