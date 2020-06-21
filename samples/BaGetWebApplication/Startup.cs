using BaGet.Core;
using BaGet.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BaGetWebApplication
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<BaGetOptions>(options =>
            {
                options.Database.ConnectionString = "Data Source=baget.db";
            });

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
