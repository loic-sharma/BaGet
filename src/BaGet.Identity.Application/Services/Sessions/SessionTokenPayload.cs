using System.Text.Json.Serialization;

namespace BaGet.Identity.Application.Services.Sessions;

/// <summary>What a session token holds before it is protected (see <see cref="SessionTokenService"/>).</summary>
internal sealed class SessionTokenPayload
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("sub")]
    public string? UserId { get; set; }

    [JsonPropertyName("stamp")]
    public string? SecurityStamp { get; set; }

    [JsonPropertyName("auth")]
    public long AuthenticatedAt { get; set; }

    [JsonPropertyName("exp")]
    public long Expires { get; set; }
}
