using System.Collections.Generic;
using System.Text.Json.Serialization;
using BaGet.Validation.Signing;

namespace BaGet.Validation.Models;

public class RepositorySignaturesResponse
{
    [JsonPropertyName("allRepositorySigned")]
    public bool AllRepositorySigned { get; set; }

    [JsonPropertyName("signingCertificates")]
    public IReadOnlyList<RepositorySigningCertificate>? SigningCertificates { get; set; }
}
