namespace BaGet.Identity.Application.Models.Account;

/// <summary>What the "sign in again" prompt shows.</summary>
public record ReauthenticateView(bool Done, bool Available, string ReturnUrl);
