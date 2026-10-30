using System.Text.Json.Serialization;

namespace BaGet.Validation.Models;

public class ValidationStep
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
