using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using BaGet.Tests.Support;
using Microsoft.IdentityModel.Tokens;

namespace BaGet.Tests.Fixtures
{
    public class BaGetTokenAuthenticatedServerFixture : BaGetServerFixture
    {

        public static string CreateToken(string securityKey)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(securityKey);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[]
                {
                                new Claim(ClaimTypes.Name, "myuserid")
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)

            };
            tokenDescriptor.Audience = "myAudience";
            tokenDescriptor.Issuer = "me";
            tokenDescriptor.IssuedAt = DateTime.UtcNow;
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        /// <summary>
        /// the secret used to create and validate tokens (the Issuer Signing Key)
        /// minimum length 55 characters to prevent exceptions from the framework!
        /// </summary>
        public static readonly string SymmetricSecurityKey = "MySecret12345abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRST";

        protected override void OnAfterNuGetLikeAuthenticationHandlerCreated(NuGetLikeAuthenticationHandler handler)
        {
            handler.Credential = new NetworkCredential("dummyUserName", CreateToken(SymmetricSecurityKey));
        }

        protected override ITestServerBuilder GetBuilder()
        {
            return TestServerBuilder.Create().UseJwtBearerAuthentication(SymmetricSecurityKey);
        }
    }
}
