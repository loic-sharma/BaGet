using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BaGet.Web.V3.Models.Protocols;

public class DeprecationRequest
{
    [JsonPropertyName("reasons")]
    public IReadOnlyList<string>? Reasons { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("alternatePackageId")]
    public string? AlternatePackageId { get; set; }

    [JsonPropertyName("alternateVersionRange")]
    public string? AlternateVersionRange { get; set; }
}
