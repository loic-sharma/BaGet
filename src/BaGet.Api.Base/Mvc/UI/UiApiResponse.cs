using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Frontend.Mvc.UI;

/// <summary>What the in-process call of a JSON twin produced: its <see cref="ScreenResult{T}"/>, or the plain action result it answered with.</summary>
/// <typeparam name="T">The screen's model.</typeparam>
public sealed class UiApiResponse<T>
{
    internal UiApiResponse(ScreenResult<T> screen, IActionResult? raw)
    {
        Screen = screen;
        Raw = raw;
    }

    /// <summary>The decision (for a plain action result, the one it stands for: 404 is not found, 401 sign in, 403 forbidden).</summary>
    public ScreenResult<T> Screen { get; }

    /// <summary>
    /// The action result the twin returned when it was not a <see cref="ScreenResult{T}"/> (<c>NotFound()</c>, <c>Forbid()</c>, <c>Challenge()</c>,
    /// a status code): a view controller returns it unchanged, so the answer is the one the twin gave.
    /// </summary>
    public IActionResult? Raw { get; }
}
