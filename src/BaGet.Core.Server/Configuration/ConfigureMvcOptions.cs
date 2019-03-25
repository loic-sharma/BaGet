using BaGet.Core.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Options;

namespace BaGet.Configuration
{
    public class ConfigureMvcOptions : IConfigureOptions<MvcOptions>
    {
        private readonly BaGetOptions _options;

        public ConfigureMvcOptions(IOptions<BaGetOptions> options)
        {
            _options = options.Value;
        }

        public void Configure(MvcOptions options)
        {
            // Allow the configuration to disable authentication.
            if (_options.FeedAuthentication.Type == AuthenticationType.None)
            {
                options.Filters.Add(new AllowAnonymousFilter());
            }
        }
    }
}
