using System;
using BaGet.Configuration;
using BaGet.Core;
using BaGet.Core.Server.Extensions;
using BaGet.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Microsoft.AspNetCore.SpaServices.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
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
                var spaStaticFilesService = app.ApplicationServices.GetService<ISpaStaticFileProvider>();
                spa.Options.DefaultPageStaticFileOptions = new StaticFileOptions();
                spa.Options.DefaultPageStaticFileOptions.FileProvider = spaStaticFilesService.FileProvider;
                if (env.IsDevelopment())
                {
                    spa.UseReactDevelopmentServer(npmScript: "start");
                }
            });
            
        }
    }
}
