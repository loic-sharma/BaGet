using System;
using BaGet.Core;
using BaGet.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection AddBaGetWebApplication(
            this IServiceCollection services,
            Action<BaGetApplication> configureAction)
        {
            services
                .AddRouting(options => options.LowercaseUrls = true)
                .AddControllers()
                .AddApplicationPart(typeof(PackageContentController).Assembly)
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0)
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.IgnoreNullValues = true;
                });

            services.AddRazorPages()
                // TODO: Only add during development, otherwise this adds filewatchers on production
                // See: https://docs.microsoft.com/en-us/aspnet/core/mvc/views/view-compilation?view=aspnetcore-5.0&tabs=netcore-cli#conditionally-enable-runtime-compilation-in-an-existing-project-1
                .AddRazorRuntimeCompilation();

            services.AddSingleton<IConfigureOptions<MvcRazorRuntimeCompilationOptions>, ConfigureRazorRuntimeCompilation>();

            services.AddHttpContextAccessor();
            services.AddTransient<IUrlGenerator, BaGetUrlGenerator>();

            services.AddBaGetApplication(configureAction);

            return services;
        }
    }
}
