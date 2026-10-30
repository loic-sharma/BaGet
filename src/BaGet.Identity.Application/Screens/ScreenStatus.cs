namespace BaGet.Identity.Application.Screens;

/// <summary>What a use case decided about a screen.</summary>
public enum ScreenStatus
{
    /// <summary>The screen is shown (or the action succeeded and the screen is shown again).</summary>
    Ok,

    /// <summary>The input was refused; the model and <see cref="IScreenResult.Errors"/> say why.</summary>
    Invalid,

    /// <summary>There is no such screen or item (also the answer to a caller who may not know it exists).</summary>
    NotFound,

    /// <summary>The caller is known but may not do this.</summary>
    Forbidden,

    /// <summary>The caller must sign in first.</summary>
    SignIn,

    /// <summary>The action is done; go to <see cref="IScreenResult.Next"/>.</summary>
    Next,
}
