using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BaGet.Validation.Signing;

public class RepositorySigningCertificate
{
    [JsonPropertyName("fingerprints")]
    public IReadOnlyDictionary<string, string>? Fingerprints { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    [JsonPropertyName("notBefore")]
    public DateTime NotBefore { get; set; }

    [JsonPropertyName("notAfter")]
    public DateTime NotAfter { get; set; }

    [JsonPropertyName("contentUrl")]
    public string? ContentUrl { get; set; }
}
