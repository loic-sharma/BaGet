using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SpaServices;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Hosting
{
    public static class ISpaBuilderExtensions
    {
        public static void UseBaGetFileProvider(this ISpaBuilder spa)
        {
            var services = spa.ApplicationBuilder.ApplicationServices;
            var fileProvider = services.GetRequiredService<BaGetSpaFileProvider>();

            spa.Options.DefaultPageStaticFileOptions = new StaticFileOptions
            {
                FileProvider = fileProvider
            };
        }
    }
}
