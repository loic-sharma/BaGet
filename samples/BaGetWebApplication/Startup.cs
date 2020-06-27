using BaGet;
using BaGet.Core;
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
                // Use SQLite as BaGet's database and store packages on the local file system.
                app.AddSqliteDatabase();
                app.AddFileStorage();
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
                // Add BaGet's endpoints.
                endpoints.MapBaGetRoutes();

                // Add a "welcome" endpoint to help you find thep package source.
                // This is optional, you can remove this endpoint if you'd like.
                endpoints.MapGet("/", async context =>
                {
                    var url = context.RequestServices.GetRequiredService<IUrlGenerator>();
                    var packageSource = url.GetServiceIndexUrl();

                    await context.Response.WriteAsync($"Package source URL: '{packageSource}'");
                });
            });
        }
    }
}
