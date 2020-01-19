using System;
using BaGet.Configuration;
using BaGet.Core;
using BaGet.Core.Server.Extensions;
using BaGet.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.NewtonsoftJson;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
                configuration.RootPath = "BaGet.UI/build";
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseStatusCodePages();
            }

            // Run migrations if necessary.
            var options = Configuration.Get<BaGetOptions>();
            if (options.RunMigrationsAtStartup && options.Database.Type != DatabaseType.AzureTable)
            {
                using (var scope = app.ApplicationServices.CreateScope())
                {
                    scope.ServiceProvider
                        .GetRequiredService<IContext>()
                        .Database
                        .Migrate();
                }
            }

            app.UsePathBase(options.PathBase);
            app.UseForwardedHeaders();
            app.UseSpaStaticFiles();

            app.UseCors(ConfigureCorsOptions.CorsPolicy);
            app.UseOperationCancelledMiddleware();

            app.UseMvc(routes =>
            {
                routes
                    .MapServiceIndexRoutes()   
                    .MapPackagePublishRoutes() 
                    .MapSymbolRoutes()         
                    .MapSearchRoutes()         
                    .MapPackageMetadataRoutes()
                    .MapPackageContentRoutes();
            });

            app.UseSpa(spa =>
            {
                spa.Options.SourcePath = "../BaGet.UI";

                if (env.IsDevelopment())
                {
                    spa.UseReactDevelopmentServer(npmScript: "start");
                }
            });
        }
    }
}
