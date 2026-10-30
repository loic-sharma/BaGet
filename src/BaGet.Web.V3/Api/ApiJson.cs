using System.Text.Json;
using System.Text.Json.Serialization;

namespace BaGet.Web.V3.Api;

/// <summary>The JSON the API writes: camelCase, enums as their names, nulls left out.</summary>
public static class ApiJson
{
    /// <summary>The options of every API response (the protocol endpoints keep the host's own).</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
