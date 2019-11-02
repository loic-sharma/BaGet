using System;
using System.Collections.Generic;
using BaGet.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


namespace BaGet.Configuration
{
    public class ConfigureForwardedHeadersOptions : IConfigureOptions<ForwardedHeadersOptions>
    {
        private IOptions<BaGetOptions> _options;
        private ILogger<ConfigureForwardedHeadersOptions> _logger;
        public ConfigureForwardedHeadersOptions(IOptions<BaGetOptions> bagetOptions, ILogger<ConfigureForwardedHeadersOptions> logger)
        {
            _options = bagetOptions;
            _logger = logger;
        }

        public void Configure(ForwardedHeadersOptions options)
        {
            if (!string.IsNullOrEmpty(_options.Value.HostUrl))
            {
                _logger.LogDebug("Allowing host URL {HostUrl}", _options.Value.HostUrl);
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                options.AllowedHosts = new List<string>() { _options.Value.HostUrl};
            }
            else
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            }
            // Do not restrict to local network/proxy
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        }
    }
}
