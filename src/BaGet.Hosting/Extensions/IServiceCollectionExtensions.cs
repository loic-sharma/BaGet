using System;
using System.IO;
using BaGet.Core;
using BaGet.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace BaGet
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection AddBaGetSpaStaticFiles(
            this IServiceCollection services)
        {
            services.AddSingleton(serviceProvider =>
            {
                var baGetOptions = serviceProvider.GetRequiredService<IOptions<BaGetOptions>>();
                var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();

                var rootPath = environment.IsDevelopment() ?
                    "../BaGet.UI/build" :
                    "BaGet.UI/build";

                var absoluteRootPath = new DirectoryInfo(Path.Combine(environment.ContentRootPath, rootPath));

                return new BaGetSpaFileProvider(absoluteRootPath, baGetOptions);
            });

            return services;
        }

        public static IServiceCollection AddBaGetWebApplication(
            this IServiceCollection services,
            Action<BaGetApplication> configureAction)
        {
            services
                .AddControllers()
                .AddApplicationPart(typeof(PackageContentController).Assembly)
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0)
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.IgnoreNullValues = true;
                });

            services.AddHttpContextAccessor();
            services.AddTransient<IUrlGenerator, BaGetUrlGenerator>();

            services.AddBaGetApplication(configureAction);

            return services;
        }
    }
}
