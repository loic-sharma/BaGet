namespace BaGet.Identity.Application.Screens;

/// <summary>The model of a screen that shows nothing but itself (the sign-out button), or of an action that only continues elsewhere.</summary>
public sealed class EmptyScreen
{
    /// <summary>The one instance.</summary>
    public static EmptyScreen Instance { get; } = new();
}
