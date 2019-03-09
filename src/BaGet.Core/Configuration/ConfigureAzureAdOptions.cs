using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace BaGet.Core.Configuration
{
    /// <summary>
    /// this class translates our own AzureAdOptions (normally deserialized from appsettings.com) into JwtBearerOptions
    /// </summary>
    public class ConfigureAzureAdOptions : IConfigureNamedOptions<JwtBearerOptions>
    {
        private readonly AzureAdOptions _azureOptions;

        public ConfigureAzureAdOptions(IOptions<AzureAdOptions> azureOptions)
        {
            _azureOptions = azureOptions.Value;
        }

        public void Configure(string name, JwtBearerOptions options)
        {
            if (!string.IsNullOrEmpty(_azureOptions.Audience))
            {
                options.Audience = _azureOptions.Audience;
            }
            else
            {
                options.Audience = _azureOptions.ClientId; //some (official) sample is working in this way needs more verification
            }
            options.Authority = $"{_azureOptions.Instance}{_azureOptions.TenantId}";

            //this may be the right place for customizing the Token Validation process, but be carefult to not break security - default setting (=best security) for all this = true
            options.TokenValidationParameters.ValidateAudience = _azureOptions.ValidateAudience;
            options.TokenValidationParameters.ValidateIssuer = _azureOptions.ValidateIssuer;
            options.TokenValidationParameters.ValidateLifetime = _azureOptions.ValidateLifetime;
        }

        public void Configure(JwtBearerOptions options)
        {
            Configure(Options.DefaultName, options);
        }
    }
}
