using BaGet.Configuration;
using BaGet.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace BaGet.Core.Server.Extensions
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureHttpServices(this IServiceCollection services)
        {
            services
                .AddControllers()
                .AddApplicationPart(typeof(PackageContentController).Assembly)
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0)
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                });

            services.AddCors();
            services.AddHttpContextAccessor();
            services.AddSingleton<IConfigureOptions<CorsOptions>, ConfigureCorsOptions>();
            services.AddSingleton<IConfigureOptions<ForwardedHeadersOptions>, ConfigureForwardedHeadersOptions>();
            services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = int.MaxValue;
            });

            return services;
        }
    }
}
