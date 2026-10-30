using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Frontend.Mvc.Screens;

/// <summary>
/// What an account workflow decided, independent of the programming model that runs it: show the screen again, or leave for
/// somewhere. <see cref="ToResult"/> turns it into the same <see cref="IActionResult"/> a controller action returns, so the workflow holds the rules and the host holds only the plumbing.
/// </summary>
internal sealed class ScreenOutcome
{
    private readonly ScreenOutcomeKind _kind;
    private readonly string? _text;
    private readonly AuthenticationProperties? _properties;
    private readonly int _status;

    private ScreenOutcome(ScreenOutcomeKind kind, string? text = null, AuthenticationProperties? properties = null, int status = 0)
    {
        _kind = kind;
        _text = text;
        _properties = properties;
        _status = status;
    }

    /// <summary>Render the screen (<c>Page()</c> / <c>View(model)</c>).</summary>
    public static readonly ScreenOutcome Show = new(ScreenOutcomeKind.Show);

    /// <summary>404.</summary>
    public static readonly ScreenOutcome NotFound = new(ScreenOutcomeKind.NotFound);

    /// <summary>Send the visitor to sign in.</summary>
    public static readonly ScreenOutcome Challenge = new(ScreenOutcomeKind.Challenge);

    /// <summary>Send the visitor to an external provider.</summary>
    public static ScreenOutcome ChallengeWith(string scheme, AuthenticationProperties properties)
        => new(ScreenOutcomeKind.ChallengeWith, scheme, properties: properties);

    /// <summary>A local redirect to <paramref name="returnUrl"/> when it is local, else to the site root.</summary>
    public static ScreenOutcome Local(string? returnUrl) => new(ScreenOutcomeKind.Local, returnUrl);

    /// <summary>A local redirect to exactly <paramref name="url"/> (which must be local: an application-relative address).</summary>
    public static ScreenOutcome LocalExact(string url) => new(ScreenOutcomeKind.LocalExact, url);

    /// <summary>A bare status code.</summary>
    public static ScreenOutcome Status(int code) => new(ScreenOutcomeKind.Status, status: code);

    /// <summary>Turns the decision into an action result; <paramref name="show"/> renders the screen.</summary>
    public IActionResult ToResult(IUrlHelper url, Func<IActionResult> show)
    {
        switch (_kind)
        {
            case ScreenOutcomeKind.Show: return show();
            case ScreenOutcomeKind.NotFound: return new NotFoundResult();
            case ScreenOutcomeKind.Challenge: return new ChallengeResult();
            case ScreenOutcomeKind.ChallengeWith: return new ChallengeResult(_text!, _properties);
            case ScreenOutcomeKind.Local: return new LocalRedirectResult(AccountSupport.LocalTarget(url, _text)) { UrlHelper = url };
            case ScreenOutcomeKind.LocalExact: return new LocalRedirectResult(_text!) { UrlHelper = url };
            case ScreenOutcomeKind.Status: return new StatusCodeResult(_status);
            default: throw new InvalidOperationException();
        }
    }
}
