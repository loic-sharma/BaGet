using System;
using BaGet.Core;
using BaGet.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
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

        private IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            // TODO: Ideally we'd use:
            //
            //       services.ConfigureOptions<ConfigureBaGetOptions>();
            //
            //       However, "ConfigureOptions" doesn't register validations as expected.
            //       We'll instead register all these configurations manually.
            // See: https://github.com/dotnet/runtime/issues/38491
            services.AddTransient<IConfigureOptions<CorsOptions>, ConfigureBaGetOptions>();
            services.AddTransient<IConfigureOptions<FormOptions>, ConfigureBaGetOptions>();
            services.AddTransient<IConfigureOptions<ForwardedHeadersOptions>, ConfigureBaGetOptions>();
            services.AddTransient<IConfigureOptions<IISServerOptions>, ConfigureBaGetOptions>();
            services.AddTransient<IValidateOptions<BaGetOptions>, ConfigureBaGetOptions>();

            services.AddBaGetOptions<IISServerOptions>(nameof(IISServerOptions));
            services.AddBaGetWebApplication(ConfigureBaGetApplication);

            // You can swap between implementations of subsystems like storage and search using BaGet's configuration.
            // Each subsystem's implementation has a provider that reads the configuration to determine if it should be
            // activated. BaGet will run through all its providers until it finds one that is active.
            services.AddScoped(DependencyInjectionExtensions.GetServiceFromProviders<IContext>);
            services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IStorageService>);
            services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IPackageDatabase>);
            services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchService>);
            services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchIndexer>);

            services.AddSingleton<IConfigureOptions<MvcRazorRuntimeCompilationOptions>, ConfigureRazorRuntimeCompilation>();

            services.AddCors();
        }

        private void ConfigureBaGetApplication(BaGetApplication app)
        {
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

            app.UseStaticFiles();
            app.UseRouting();

            app.UseCors(ConfigureBaGetOptions.CorsPolicy);
            app.UseOperationCancelledMiddleware();

            app.UseEndpoints(endpoints =>
            {
                var baget = new BaGetEndpointBuilder();

                baget.MapEndpoints(endpoints);
            });
        }
    }
}
