using BaGet.Configuration;
using BaGet.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Reflection;

namespace BaGet.Core.Server.Extensions
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureHttpServices(this IServiceCollection services)
        {                
            // TODO: Consider replacing "AddMvc" with "AddControllers".
            // See: https://docs.microsoft.com/en-us/aspnet/core/migration/22-to-30?view=aspnetcore-3.1&tabs=visual-studio#mvc-service-registration
            // TODO: Consider replacing "AddMvc" with Endpoint Routing
            // See: options => options.EnableEndpointRouting = false
            services
                .AddControllers(options => options.EnableEndpointRouting = false)
                .AddApplicationPart(typeof(PackageContentController).Assembly)
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0)
                //.AddControllersAsServices()
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
