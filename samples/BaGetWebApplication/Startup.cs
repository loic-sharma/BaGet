using BaGet.Core;
using BaGet.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BaGetWebApplication
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddBaGetWebApplication(app =>
            {
                app.AddFilesystem();
                app.AddSqlite();
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {
                    var url = context.RequestServices.GetRequiredService<IUrlGenerator>();
                    var packageSource = url.GetServiceIndexUrl();

                    await context.Response.WriteAsync($"Package source URL: '{packageSource}'");
                });

                endpoints.MapServiceIndexRoutes();
                endpoints.MapPackagePublishRoutes();
                endpoints.MapSymbolRoutes();
                endpoints.MapSearchRoutes();
                endpoints.MapPackageMetadataRoutes();
                endpoints.MapPackageContentRoutes();
            });
        }
    }
}
