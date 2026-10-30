using System.Collections.Generic;
using System.Text.Json.Serialization;
using BaGet.Protocol.Models;

namespace BaGet.Core;

/// <summary>
/// BaGet's extensions to a registration index page. Extends
/// <see cref="RegistrationIndexPage"/>, shadowing the single property whose type
/// differs.
/// </summary>
public class BaGetRegistrationIndexPage : RegistrationIndexPage
{
    /// <summary>
    /// This was modified to use BaGet's extended registration index page item model.
    /// </summary>
    // Ordered so the serialized property order is unchanged by inheriting the base type.
    [JsonPropertyName("items")]
    [JsonPropertyOrder(1)]
    public new IReadOnlyList<BaGetRegistrationIndexPageItem>? ItemsOrNull { get; set; }
}