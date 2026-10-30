using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>What a set of patterns selects.</summary>
public sealed record PatternPreviewDto(IReadOnlyList<string> Repositories, string? TestPackage, PatternTest? Test);
