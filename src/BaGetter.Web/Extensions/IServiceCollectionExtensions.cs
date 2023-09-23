using System;
using System.Text.Json.Serialization;
using BaGetter.Core;
using BaGetter.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BaGetter
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection AddBaGetterWebApplication(
            this IServiceCollection services,
            Action<BaGetterApplication> configureAction)
        {
            services
                .AddRouting(options => options.LowercaseUrls = true)
                .AddControllers()
                .AddApplicationPart(typeof(PackageContentController).Assembly)
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                });

            services.AddRazorPages();

            services.AddHttpContextAccessor();
            services.AddTransient<IUrlGenerator, BaGetterUrlGenerator>();

            services.AddBaGetterApplication(configureAction);

            return services;
        }
    }
}
