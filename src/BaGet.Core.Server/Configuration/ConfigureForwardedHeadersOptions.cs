using BaGet.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;

namespace BaGet.Configuration
{
    public class ConfigureForwardedHeadersOptions : IConfigureOptions<ForwardedHeadersOptions>
    {
        private BaGetOptions bagetOptions;
        public ConfigureForwardedHeadersOptions(IOptions<BaGetOptions> bagetOptions)
        {
            this.bagetOptions = bagetOptions.Value;
        }

        public void Configure(ForwardedHeadersOptions options)
        {
            if (!string.IsNullOrEmpty(bagetOptions.HostUrl))
            {
                Console.WriteLine("Host Url: " + bagetOptions.HostUrl);
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                options.AllowedHosts = new List<string>() { bagetOptions.HostUrl};
            }else
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            }
            // Do not restrict to local network/proxy
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        }
    }
}
