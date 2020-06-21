using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BaGet.Core
{
    public class ConfigureBaGetOptions<TOptions> : IConfigureOptions<TOptions> where TOptions : class
    {
        private readonly IConfiguration _config;

        public ConfigureBaGetOptions(IConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Configure(TOptions options) => _config.Bind(options);
    }
}
