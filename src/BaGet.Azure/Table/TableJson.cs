using System.Text.Encodings.Web;
using System.Text.Json;

namespace BaGet.Azure;

/// <summary>
/// The JSON held in the table's string columns (authors, tags, dependencies, package
/// types, frameworks). Rows written by earlier versions used Newtonsoft.Json, so this
/// keeps that format: property names as declared, and non-ASCII text unescaped.
/// </summary>
internal static class TableJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        // Newtonsoft matched property names case-insensitively, so older rows may differ in case.
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    #pragma warning disable CS8603, CS8604
    public static T Deserialize<T>(string? json) => JsonSerializer.Deserialize<T>(json, Options);
    #pragma warning restore CS8603, CS8604
}