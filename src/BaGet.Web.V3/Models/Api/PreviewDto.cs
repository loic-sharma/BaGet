using System.Collections.Generic;

namespace BaGet.Web.V3.Models.Api;

/// <summary>What a preview found.</summary>
public sealed record PreviewDto(string PackageId, string Version, IReadOnlyList<PreviewStepDto> Steps, string? Answers);
