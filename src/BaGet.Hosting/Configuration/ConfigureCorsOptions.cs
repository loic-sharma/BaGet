using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace BaGet.Hosting
{
    public class ConfigureCorsOptions : IConfigureOptions<CorsOptions>
    {
        public const string CorsPolicy = "AllowAll";

        public void Configure(CorsOptions options)
        {
            // TODO: Consider disabling this on production builds.
            options.AddPolicy(
                CorsPolicy,
                builder => builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader());
        }
    }
}
