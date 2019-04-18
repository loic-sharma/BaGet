using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BaGet.Configuration;
using BaGet.Core.Configuration;
using BaGet.Core.Entities;
using BaGet.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet
{
    public class BaGetTool
    {

        public static string FullPath => Instance.Value._fullPath;
        public static string ConfigPath => Instance.Value._configPath;
        public static string SpaRoot => Instance.Value._spaRoot;

        private static readonly Lazy<BaGetTool> Instance = new Lazy<BaGetTool>(() => new BaGetTool());

        private readonly string _fullPath;
        private readonly string _configPath;
        private readonly string _spaRoot;

        private BaGetTool()
        {
            var assembly = Assembly.GetEntryAssembly();
            var assemblyName = assembly.GetName().Name.ToLowerInvariant();
            var assemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

            var version = NuGetVersion.Parse(assemblyVersion).ToNormalizedString().ToLowerInvariant();

            var root = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.ExpandEnvironmentVariables("%USERPROFILE%")
                : Environment.ExpandEnvironmentVariables("%HOME%");

            var toolsPath = Path.Combine(root, ".dotnet", "tools", ".store");

            // TODO: The SPA Root should be found by reading the tool's project.assets.json file.
            _fullPath = Path.Combine(toolsPath, assemblyName, version);
            _configPath = Path.Combine(_fullPath, assemblyName, version, "content", "appsettings.json");
            _spaRoot = Path.Combine(_fullPath, assemblyName, version, "tools", "netcoreapp2.2", "any", "BaGet.UI", "build");
        }
    }

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
                // Try to find the UI in the current path, but allow falling back to the tool if it installed. 
                var spaRoot = Path.Combine(Directory.GetCurrentDirectory(), "BaGet.UI", "build");

                if (Directory.Exists(spaRoot) || !Directory.Exists(BaGetTool.SpaRoot))
                {
                    configuration.RootPath = "BaGet.UI/build";
                }
                else
                {
                    configuration.RootPath = BaGetTool.SpaRoot;
                }
            });
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
    }
}
