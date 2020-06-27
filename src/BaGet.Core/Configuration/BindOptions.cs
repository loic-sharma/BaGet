using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BaGet.Core
{
    /// <summary>
    /// Automatically binds configs to options.
    /// </summary>
    /// <typeparam name="TOptions">The options to bind to.</typeparam>
    public class BindOptions<TOptions> : IConfigureOptions<TOptions> where TOptions : class
    {
        private readonly IConfiguration _config;

        /// <summary>
        /// Automatically bind these configurations to the options.
        /// </summary>
        /// <param name="config">The configs to automatically bind to options.</param>
        public BindOptions(IConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Configure(TOptions options) => _config.Bind(options);
    }
}
