using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>The result of previewing repository and package patterns.</summary>
/// <param name="Repositories">The NuGet repositories whose names match the repository patterns.</param>
/// <param name="TestPackage">The package ID typed in the test box, if any.</param>
/// <param name="Test">Whether the target would apply to <paramref name="TestPackage"/>, and why.</param>
public sealed record PatternPreview(IReadOnlyList<string> Repositories, string? TestPackage, PatternTest? Test);
