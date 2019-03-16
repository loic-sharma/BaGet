using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BaGet.Core.Configuration
{
    /// <summary>
    /// this class translates our own BaGetJwtBearerOptions (normally deserialized from appsettings.com) into JwtBearerOptions
    /// </summary>
    public class ConfigureBaGetJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
    {
        private readonly BaGetJwtBearerOptions _baGetjwtBearerOptions;

        public ConfigureBaGetJwtBearerOptions(IOptions<BaGetJwtBearerOptions> options)
        {
            _baGetjwtBearerOptions = options.Value;
        }

        public void Configure(string name, JwtBearerOptions options)
        {
            if (!string.IsNullOrEmpty(_baGetjwtBearerOptions.Audience))
            {
                options.Audience = _baGetjwtBearerOptions.Audience;
            }
            //this may be the right place for customizing the Token Validation process, but be careful to not break security - default setting (=best security) for all this = true
            options.TokenValidationParameters.ValidateAudience = _baGetjwtBearerOptions.ValidateAudience;
            options.TokenValidationParameters.ValidateIssuer = _baGetjwtBearerOptions.ValidateIssuer;
            options.TokenValidationParameters.ValidateLifetime = _baGetjwtBearerOptions.ValidateLifetime;
            if (_baGetjwtBearerOptions.ValidateIssuerSigningKey)
            {
                var key = Encoding.ASCII.GetBytes(_baGetjwtBearerOptions.SymmetricSecurityKey);//minimumlength ~55 characters. Without that we get some exceptions!
                options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(key);
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            }
            else
            {
                options.TokenValidationParameters.ValidateIssuerSigningKey = false; //strange behavior bug in jwt => false is ignored, we must add a implementation that ignores IssuerSigningKey!

                options.TokenValidationParameters.SignatureValidator = delegate (string token, TokenValidationParameters parameters)
                {
                    var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(token);

                    return jwt;
                };
            }
        }

        public void Configure(JwtBearerOptions options)
        {
            Configure(Options.DefaultName, options);
        }
    }
}
