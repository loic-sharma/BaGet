using System.Collections.Generic;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A format a repository can have.</summary>
public sealed record FormatDto(string Format, string DisplayName, IReadOnlyList<string> CreatableKinds, bool SupportsVirtual, string FeedPath, IReadOnlyList<FormatFieldDto> CreateFields);
