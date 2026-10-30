using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Html;

namespace BaGet.Identity.Application.Screens;

/// <summary>
/// An <see cref="HtmlString"/> (rendered, already safe HTML such as a readme) is written as the plain string it holds, not as an object, so a client
/// receives the HTML as text.
/// </summary>
internal sealed class HtmlStringConverter : JsonConverter<HtmlString>
{
    public override HtmlString? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : new HtmlString(reader.GetString());

    public override void Write(Utf8JsonWriter writer, HtmlString value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
