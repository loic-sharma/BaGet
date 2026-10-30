using System.Text.Json.Serialization;
using BaGet.Protocol.Models;

namespace BaGet.Core;

/// <summary>
/// BaGet's extensions to a registration index page item. Extends
/// <see cref="RegistrationIndexPageItem"/>, shadowing the single property whose type
/// differs.
/// </summary>
public class BaGetRegistrationIndexPageItem : RegistrationIndexPageItem
{
    /// <summary>
    /// This was modified to use BaGet's extended package metadata model.
    /// </summary>
    // Ordered so the serialized property order is unchanged by inheriting the base type.
    [JsonPropertyName("catalogEntry")]
    [JsonPropertyOrder(1)]
    public new BaGetPackageMetadata? PackageMetadata { get; set; }
}