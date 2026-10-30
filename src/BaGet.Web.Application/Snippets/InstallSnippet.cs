using System.Collections.Generic;

namespace BaGet.Web.Application.Snippets;

/// <summary>One tab of the "install / configure / push" commands of a package, artifact or image page (one tool per tab).</summary>
public sealed class InstallSnippet
{
    /// <summary>The tool the tab is for.</summary>
    public string? Name { get; init; }

    /// <summary>A shell prompt shown before the first line; not part of the copied text.</summary>
    public string? Prefix { get; init; }

    /// <summary>The lines to copy.</summary>
    public IReadOnlyList<string>? Lines { get; init; }
}
