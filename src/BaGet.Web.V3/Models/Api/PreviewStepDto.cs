using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>One member's answer.</summary>
public sealed record PreviewStepDto(string Member, ResolutionOutcome Outcome);
