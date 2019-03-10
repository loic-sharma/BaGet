using System;
using System.Net;
using BaGet.Configuration;
using BaGet.Core.Configuration;
using BaGet.Core.Entities;
using BaGet.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
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

            UseHttps = configuration.Get<BaGetOptions>().Https != null;
        }

        public static bool UseHttps { get; private set; }

        public static KestrelServerOptions KestrelServerOptions { get; set; }

        public IConfiguration Configuration { get; }

        private BaGetOptions BaGetConfiguration => Configuration.Get<BaGetOptions>();

        public void ConfigureServices(IServiceCollection services)
        {
            services.ConfigureBaGet(Configuration, httpServices: true);

            // In production, the UI files will be served from this directory
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "BaGet.UI/build";
            });

            if (UseHttps)
            {
                services.AddHttpsRedirection(options =>
                {
                    options.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect;
                    options.HttpsPort = BaGetConfiguration.Https.Port;
                });
            }
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
            if (options.RunMigrationsAtStartup)
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

            if (UseHttps)
            {
                ConfigureHttps();
            }

            app.UseCors(ConfigureCorsOptions.CorsPolicy);

            app.UseMvc(routes =>
            {
                routes
                    .MapServiceIndexRoutes()
                    .MapPackagePublishRoutes()
                    .MapSymbolRoutes()
                    .MapSearchRoutes()
                    .MapRegistrationRoutes()
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

        private void ConfigureHttps()
        {
            KestrelServerOptions.Listen(IPAddress.Loopback, BaGetConfiguration.Https.Port, listenOptions =>
            {
                listenOptions.UseHttps(BaGetConfiguration.Https.CertificateFileName, BaGetConfiguration.Https.CertificatePassword);
            });
        }
    }
}
