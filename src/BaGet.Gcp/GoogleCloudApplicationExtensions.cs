using BaGet.Gcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BaGet.Core
{
    public static class GoogleCloudApplicationExtensions
    {
        public static void AddGoogleCloudStorage(this BaGetApplication app)
        {
            app.Services.AddBaGetOptions<GoogleCloudStorageOptions>(nameof(BaGetOptions.Storage));
            app.Services.AddTransient<GoogleCloudStorageService>();

            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<GoogleCloudStorageService>());
        }
    }
}
