using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BaGet.Identity.Application.Screens;

/// <summary>The model-independent part of a <see cref="ScreenResult{T}"/>, for the code that maps a result to HTTP or to a view.</summary>
public interface IScreenResult
{
    /// <summary>The decision.</summary>
    ScreenStatus Status { get; }

    /// <summary>Field name to messages; the empty name holds the messages of the form as a whole.</summary>
    IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>Where to go, when <see cref="Status"/> is <see cref="ScreenStatus.Next"/>.</summary>
    ScreenNext? Next { get; }

    /// <summary>The HTTP status a view renders with when it is not 200 (an upload refused renders its form again with 400).</summary>
    int? StatusCode { get; }

    /// <summary>The model, or null.</summary>
    [JsonIgnore]
    object? ModelObject { get; }
}
