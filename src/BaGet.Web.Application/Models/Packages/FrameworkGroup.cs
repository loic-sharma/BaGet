using System.Collections.Generic;

namespace BaGet.Web.Application.Models.Packages;

/// <summary>
/// The frameworks of one package, as the package page's Frameworks tab shows them.
/// </summary>
public sealed class FrameworkGroup
{
    /// <summary>The family (.NET, .NET Core, .NET Standard, .NET Framework, Other).</summary>
    public string? Name { get; init; }

    /// <summary>Frameworks the package ships assets for.</summary>
    public List<string> Declared { get; } = new();

    /// <summary>Frameworks the package works with without declaring them.</summary>
    public List<string> Computed { get; } = new();
}
