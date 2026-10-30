using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace BaGet.Tests;

/// <summary>
/// Stands in for an external provider's answer: <c>GET /test/external?sub=&amp;name=&amp;email=&amp;verified=&amp;xsrf=&amp;auth_time=&amp;provider=&amp;c=type=value</c>
/// fills the external cookie the way the provider's callback does. <c>xsrf</c> is the value
/// the sign-in challenge was started with (the user id, for a re-authentication).
/// </summary>
internal sealed class ExternalCookieFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            if (context.Request.Path != "/test/external")
            {
                await continuation();
                return;
            }

            var query = context.Request.Query;
            #pragma warning disable CS8604
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, query["sub"]),
                new("preferred_username", query["name"]),
            };
            if (!string.IsNullOrEmpty(query["email"])) claims.Add(new Claim(ClaimTypes.Email, query["email"]));
            if (!string.IsNullOrEmpty(query["verified"])) claims.Add(new Claim("email_verified", query["verified"]));
            #pragma warning restore CS8604

            #pragma warning disable CS8604
            if (!string.IsNullOrEmpty(query["auth_time"])) claims.Add(new Claim("auth_time", query["auth_time"]));
            #pragma warning restore CS8604

            // Extra claims (c=type=value, repeatable) as a provider with custom claims would send them.
            foreach (var extra in query["c"])
            {
                var at = extra?.IndexOf('=') ?? -1;
                if (at <= 0) continue;

                // The OpenID Connect handler renames incoming claim types (MapInboundClaims): role and roles become ClaimTypes.Role, group a URI, groups stays.
                var raw = extra![..at];
                claims.Add(new Claim(Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler.DefaultInboundClaimTypeMap.TryGetValue(raw, out var mapped) ? mapped : raw, extra[(at + 1)..]));
            }

            var properties = new AuthenticationProperties();
            properties.Items["LoginProvider"] = string.IsNullOrEmpty(query["provider"]) ? "oidc" : query["provider"].ToString();
            if (!string.IsNullOrEmpty(query["xsrf"])) properties.Items["XsrfId"] = query["xsrf"];
            await context.SignInAsync(
                IdentityConstants.ExternalScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                properties);

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        next(app);
    };
}
