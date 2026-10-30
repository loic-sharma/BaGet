using Microsoft.AspNetCore.Authentication;

namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>Where a browser is sent to talk to the external provider.</summary>
/// <param name="Scheme">The authentication scheme to challenge.</param>
/// <param name="Properties">The challenge's properties (callback address, anti-forgery value, prompt).</param>
public sealed record ExternalChallenge(string Scheme, AuthenticationProperties Properties);
