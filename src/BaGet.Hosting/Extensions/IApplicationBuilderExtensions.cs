using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Hosting
{
    public static class IApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseBaGetSpaStaticFiles(this IApplicationBuilder app)
        {
            var fileProvider = app.ApplicationServices.GetRequiredService<BaGetSpaFileProvider>();

            app.UseSpaStaticFiles(new StaticFileOptions
            {
                FileProvider = fileProvider
            });

            return app;
        }

        public static IApplicationBuilder UseOperationCancelledMiddleware(this IApplicationBuilder app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            return app.UseMiddleware<OperationCancelledMiddleware>();
        }
    }
}
