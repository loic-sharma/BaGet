using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BaGet.Validation.Models;

public class ValidationStatusResponse
{
    [JsonPropertyName("submissionId")]
    public string? SubmissionId { get; set; }

    /// <summary>
    /// "Validating", "Available" or "Quarantined".
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("steps")]
    public IReadOnlyList<ValidationStep>? Steps { get; set; }
}
