using System.Collections.Generic;
using System.Text.Json.Serialization;
using BaGet.Protocol.Models;

namespace BaGet.Core;

/// <summary>
/// BaGet's extensions to a registration index response. Extends
/// <see cref="RegistrationIndexResponse"/>, shadowing the property whose type differs
/// and adding BaGet's own total download count.
/// </summary>
public class BaGetRegistrationIndexResponse : RegistrationIndexResponse
{
    /// <summary>
    /// The pages that contain all of the versions of the package, ordered
    /// by the package's version. This was modified to use BaGet's extended
    /// registration index page model.
    /// </summary>
    // Ordered so the serialized property order is unchanged by inheriting the base type.
    [JsonPropertyName("items")]
    [JsonPropertyOrder(1)]
    public new IReadOnlyList<BaGetRegistrationIndexPage>? Pages { get; set; }

    /// <summary>
    /// The package's total downloads across all versions.
    /// This is not part of the official NuGet protocol.
    /// </summary>
    [JsonPropertyName("totalDownloads")]
    [JsonPropertyOrder(2)]
    public long TotalDownloads { get; set; }
}