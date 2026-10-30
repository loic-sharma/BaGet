using System;

namespace BaGet.Frontend.Mvc.Attributes;

/// <summary>
/// Marks the action of a screen that only answers a request no handler of the screen claims (a stray <c>POST</c> without a known
/// <c>?handler=</c>, answered 404 after the antiforgery check). It is not one of the screen's handlers, so it is no part of the screen's
/// routes or of the UI API parity; see <see cref="PageHandlerAttribute"/> and <c>ScreenEndpoints</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class FallbackHandlerAttribute : Attribute
{
}
