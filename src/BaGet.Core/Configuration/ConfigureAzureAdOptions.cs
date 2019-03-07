using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace BaGet.Core.Configuration
{
    public class ConfigureAzureAdOptions : IConfigureNamedOptions<JwtBearerOptions>
    {
        private readonly AzureAdOptions _azureOptions;

        public ConfigureAzureAdOptions(IOptions<AzureAdOptions> azureOptions)
        {
            _azureOptions = azureOptions.Value;
        }

        public void Configure(string name, JwtBearerOptions options)
        {
            options.Audience = _azureOptions.ClientId;
            options.Authority = $"{_azureOptions.Instance}{_azureOptions.TenantId}";
        }

        public void Configure(JwtBearerOptions options)
        {
            Configure(Options.DefaultName, options);
        }
    }
}
