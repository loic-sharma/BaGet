using System.Collections.Generic;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A field a format asks for when creating a local repository.</summary>
public sealed record FormatFieldDto(string Name, string Label, string? Help, bool Required, IReadOnlyList<string>? Options);
