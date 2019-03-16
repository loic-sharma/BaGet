using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace BaGet.Core.Configuration
{
    public class AzureAdOptions 
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Instance { get; set; }
        public string Domain { get; set; }
        public string TenantId { get; set; }
        public string Audience { get; set; }
        public bool ValidateAudience { get; set; } = true;
        public bool ValidateIssuer { get; set; } = true;
        public bool ValidateLifetime { get; set; } = true;
    }
}
