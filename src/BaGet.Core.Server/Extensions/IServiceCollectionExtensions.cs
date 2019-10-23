using System;
using BaGet.Configuration;
using BaGet.Core.Server.FileProvider;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SpaServices.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace BaGet.Core.Server.Extensions
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureHttpServices(this IServiceCollection services)
        {
            services
                .AddMvc()
                .AddApplicationPart(typeof(BaGet.Controllers.PackageContentController).Assembly)
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_2)
                .AddJsonOptions(options =>
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

        public static IServiceCollection AddBagetSpaStatics(this IServiceCollection services)
        {
           
                services.AddSingleton<ISpaStaticFileProvider>(serviceProvider => {

                    var env = serviceProvider.GetRequiredService<IHostingEnvironment>();
                    var RootPath = "BaGet.UI/build";
                    if(env.IsDevelopment())
                    {
                        RootPath = "../" + RootPath;
                    }

                    return new BaGetStaticFileProvider(serviceProvider, RootPath, serviceProvider.GetRequiredService<ILogger<BaGetStaticFileProvider>>());
                    });
            
            return services;
        }
    }
}
