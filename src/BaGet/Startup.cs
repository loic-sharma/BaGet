using System;
using BaGet.Core;
using BaGet.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            // In production, the UI files will be served from this directory
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "BaGet.UI/build";
            });

            services.AddBaGetWebApplication(app =>
            {
                // You can swap between implementations of subsystems like storage and search using BaGet's configuration.
                // Each subsystem's implementation has a provider that reads the configuration to determine if it should be
                // activated. BaGet will run through all its providers until it finds one that is active.
                // NOTE: Don't copy this if you are embedding BaGet into your own ASP.NET Core application.
                app.Services.AddScoped(DependencyInjectionExtensions.GetServiceFromProviders<IContext>);
                app.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IStorageService>);
                app.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IPackageService>);
                app.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchService>);
                app.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchIndexer>);

                // Add database providers.
                app.AddAzureTableDatabase();
                app.AddMySqlDatabase();
                app.AddPostgreSqlDatabase();
                app.AddSqliteDatabase();
                app.AddSqlServerDatabase();

                // Add storage providers.
                app.AddFileStorage();
                app.AddAliyunOssStorage();
                app.AddAwsS3Storage();
                app.AddAzureBlobStorage();
                app.AddGoogleCloudStorage();

                // Add search providers.
                app.AddAzureSearch();

                // Add strict validation for BaGet's configs.
                app.Services.AddSingleton<IValidateOptions<BaGetOptions>, ValidateBaGetOptions>();
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var options = Configuration.Get<BaGetOptions>();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseStatusCodePages();
            }

            app.UseForwardedHeaders();
            app.UsePathBase(options.PathBase);

            app.UseSpaStaticFiles();

            app.UseRouting();

            app.UseCors(ConfigureCorsOptions.CorsPolicy);
            app.UseOperationCancelledMiddleware();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBaGetRoutes();
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
