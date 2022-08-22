using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;

namespace BaGet.Web;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddBaGetWebApplication(this IServiceCollection services, Action<BaGetApplication> configureAction)
    {
        services.AddRouting(options => options.LowercaseUrls = true)
                .AddControllers()
                .AddApplicationPart(typeof(PackageContentController).Assembly)
                .AddJsonOptions(options => options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

        services.AddRazorPages();

        services.AddHttpContextAccessor();
        services.AddTransient<IUrlGenerator, BaGetUrlGenerator>();

        services.AddBaGetApplication(configureAction);

        return services;
    }
}
