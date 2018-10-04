using System;
using System.IO;
using BaGet.Configurations;
using BaGet.Core.Configuration;
using BaGet.Core.Entities;
using BaGet.Extensions;
using BaGet.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.ConfigureBaGet(Configuration, httpServices: true);

            // In production, the UI files will be served from this directory
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = Path.Combine("BaGet.UI", "dist");
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            var scopeFactory = app.ApplicationServices
                .GetRequiredService<IServiceScopeFactory>();

            using (var scope = scopeFactory.CreateScope())
            {
                app.UseDeveloperExceptionPage();
                app.UseStatusCodePages();
            }

            // Run migrations if enabled
            var databaseOptions = app.ApplicationServices.GetRequiredService<IOptions<BaGetOptions>>()
                    .Value
                    .Database;
            if(databaseOptions.RunMigrations) {
                using (var scope = scopeFactory.CreateScope())
                {
                    scope.ServiceProvider
                        .GetRequiredService<IContext>()
                        .Database
                        .Migrate();
                }
            }

            app.UseForwardedHeaders();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseSpaStaticFiles();

            app.UseCors(ConfigureCorsOptions.CorsPolicy);

            app.UseMvc(routes =>
            {
                routes
                    .MapServiceIndexRoutes()
                    .MapPackagePublishRoutes()
                    .MapSearchRoutes()
                    .MapRegistrationRoutes()
                    .MapPackageContentRoutes();
            });

            app.UseSpa(spa =>
            {
                if (env.IsDevelopment())
                {
                    spa.UseProxyToSpaDevelopmentServer("http://localhost:1234");
                }
            });
        }
    }
}
