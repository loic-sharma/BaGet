using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BaGet.Core.Authentication;
using BaGet.Core.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using BaGet.Core.Services;

namespace BaGet.Extensions
{
    public class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationOptions>
    {

        private readonly IUserValidationService _authenticationService;

        public BasicAuthenticationHandler(
            IOptionsMonitor<BasicAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            IUserValidationService authenticationService)
            : base(options, logger, encoder, clock)
        {
            _authenticationService = authenticationService;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(HeaderNames.Authorization))
            {
                //Authorization header not in request
                return AuthenticateResult.NoResult();
            }

            if (!AuthenticationHeaderValue.TryParse(Request.Headers[HeaderNames.Authorization], out var headerValue))
            {
                //Invalid Authorization header
                return AuthenticateResult.NoResult();
            }

            if (!BasicAuthenticationDefaults.AuthenticationScheme.Equals(headerValue.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                //Not Basic authentication header
                return AuthenticateResult.NoResult();
            }

            if (AuthenticationHeaderValueExtensions.TryGetNetworkCredentialFromHeader(headerValue, out var credential) == false)
            {
                Logger.LogTrace("Request Header contains 'Authorization' but it does not returns credentials");
                return AuthenticateResult.NoResult();
            }

            bool isValidUser = await _authenticationService.IsValidUserAsync(credential);

            if (!isValidUser)
            {
                return AuthenticateResult.Fail("Invalid username or password");
            }

            var claims = new[] { new Claim(ClaimTypes.Name, credential.UserName) };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return AuthenticateResult.Success(ticket);
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var header = $"Basic charset=\"UTF-8\"";
            if (string.IsNullOrEmpty(Options.Realm)==false)
            {
                header = $"Basic realm=\"{Options.Realm}\", charset=\"UTF-8\"";
            }
            Response.Headers[HeaderNames.WWWAuthenticate] = header;
            await base.HandleChallengeAsync(properties);
        }
    }
}
