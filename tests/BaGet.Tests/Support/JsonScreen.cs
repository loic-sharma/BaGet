using System.Text.Json.Nodes;

namespace BaGet.Tests;

/// <summary>Reads the screen of a <c>next</c> answer of the JSON UI API (<c>{ "controller": ..., "action": ... }</c>) as <c>Controller/Action</c>.</summary>
internal static class JsonScreen
{
    public static string Name(JsonNode screen) => screen["controller"]!.GetValue<string>() + "/" + screen["action"]!.GetValue<string>();
}
