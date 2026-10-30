using System;
using System.Collections.Generic;

namespace BaGet.Identity.Application.Models.Repositories;

/// <summary>What the formats screen (<c>/admin/formats</c>) lists: the package formats this server can host.</summary>
public sealed class FormatsViewModel
{
    /// <summary>The formats.</summary>
    public IReadOnlyList<FormatRow> Rows { get; internal set; } = Array.Empty<FormatRow>();
}
