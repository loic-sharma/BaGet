using System;
using System.Collections.Generic;

namespace BaGet.Identity.Application.Screens;

/// <summary>
/// What a use case of a screen returns: independent of the programming model that runs it. The JSON API serialises it as it is
/// (<c>status</c>, <c>model</c>, <c>errors</c>, <c>next</c>); the view controllers turn it into a view, an error status or a redirect.
/// A use case holds the rules and never sees <c>HttpContext</c>, <c>ModelState</c> or <c>TempData</c>.
/// </summary>
/// <typeparam name="T">The screen's model (a plain DTO).</typeparam>
public sealed class ScreenResult<T> : IScreenResult
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    private ScreenResult(ScreenStatus status, T? model, IReadOnlyDictionary<string, string[]>? errors, ScreenNext? next, int? statusCode)
    {
        Status = status;
        Model = model;
        Errors = errors ?? NoErrors;
        Next = next;
        StatusCode = statusCode;
    }

    /// <inheritdoc/>
    public ScreenStatus Status { get; }

    /// <summary>The screen's model; null for an answer that has none (not found, sign in, next).</summary>
    public T? Model { get; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <inheritdoc/>
    public ScreenNext? Next { get; }

    /// <inheritdoc/>
    public int? StatusCode { get; }

    /// <inheritdoc/>
    object? IScreenResult.ModelObject => Model;

    /// <summary>Show the screen.</summary>
    public static ScreenResult<T> Ok(T model, int? statusCode = null)
        => new(ScreenStatus.Ok, model, null, null, statusCode);

    /// <summary>The input was refused: show the screen again with the reasons.</summary>
    public static ScreenResult<T> Invalid(T model, IReadOnlyDictionary<string, string[]> errors, int? statusCode = null)
        => new(ScreenStatus.Invalid, model, errors ?? throw new ArgumentNullException(nameof(errors)), null, statusCode);

    /// <summary>The input was refused with one message about the form as a whole.</summary>
    public static ScreenResult<T> Invalid(T model, string message, int? statusCode = null)
        => Invalid(model, new Dictionary<string, string[]> { [string.Empty] = new[] { message } }, statusCode);

    /// <summary>No such screen or item.</summary>
    public static ScreenResult<T> NotFound() => new(ScreenStatus.NotFound, default, null, null, null);

    /// <summary>The caller may not do this.</summary>
    public static ScreenResult<T> Forbidden() => new(ScreenStatus.Forbidden, default, null, null, null);

    /// <summary>The caller may not do this, and <paramref name="message"/> says why (carried as the form-wide error, <c>""</c>).</summary>
    public static ScreenResult<T> Forbidden(string message)
        => new(ScreenStatus.Forbidden, default, new Dictionary<string, string[]> { [string.Empty] = new[] { message } }, null, null);

    /// <summary>The caller must sign in.</summary>
    public static ScreenResult<T> SignIn() => new(ScreenStatus.SignIn, default, null, null, null);

    /// <summary>The caller must sign in, and <paramref name="message"/> says how (carried as the form-wide error, <c>""</c>).</summary>
    public static ScreenResult<T> SignIn(string message)
        => new(ScreenStatus.SignIn, default, new Dictionary<string, string[]> { [string.Empty] = new[] { message } }, null, null);

    /// <summary>The action is done; continue at <paramref name="next"/>.</summary>
    public static ScreenResult<T> GoTo(ScreenNext next)
        => new(ScreenStatus.Next, default, null, next ?? throw new ArgumentNullException(nameof(next)), null);

    /// <summary>The same decision for a screen of another model (a gate's refusal, which has no model).</summary>
    public static ScreenResult<T> From(IScreenResult refusal)
    {
        if (refusal == null) throw new ArgumentNullException(nameof(refusal));

        return refusal.Status switch
        {
            ScreenStatus.NotFound => NotFound(),
            ScreenStatus.Forbidden => refusal.Errors.Count > 0 ? new(ScreenStatus.Forbidden, default, refusal.Errors, null, null) : Forbidden(),
            ScreenStatus.SignIn => refusal.Errors.Count > 0 ? new(ScreenStatus.SignIn, default, refusal.Errors, null, null) : SignIn(),
            ScreenStatus.Next => GoTo(refusal.Next!),
            _ => throw new ArgumentException("Only an answer without a model can be carried over.", nameof(refusal)),
        };
    }
}
