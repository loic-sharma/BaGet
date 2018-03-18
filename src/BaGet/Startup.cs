using System;
using System.Net;
using System.Net.Http;
using BaGet.Core.Entities;
using BaGet.Core.Services;
using BaGet.Entities;
using BaGet.Services.Remote;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet
{
    public class Startup
    {
        public const string IndexRouteName = "index";
        public const string UploadRouteName = "upload";
        public const string DeleteRouteName = "delete";
        public const string RelistRouteName = "relist";
        public const string SearchRouteName = "search";
        public const string AutocompleteRouteName = "autocomplete";
        public const string RegistrationIndexRouteName = "registration-index";
        public const string RegistrationLeafRouteName = "registration-leaf";
        public const string PackageVersionsRouteName = "package-versions";
        public const string PackageDownloadRouteName = "package-download";
        public const string PackageDownloadManifestRouteName = "package-download-manifest";

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
            services.Configure<Options>(Configuration);
            services.AddScoped<IContext>(p => p.GetRequiredService<Context>());
            services.AddDbContext<Context>(options => options.UseSqlite("Data Source=baget.db"));

            services.AddSingleton(s =>
            {
                var options = s.GetRequiredService<IOptions<Options>>().Value;

                var client = new HttpClient(new HttpClientHandler
                {
                    AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate),
                });

                client.Timeout = TimeSpan.FromSeconds(options.PackageDownloadTimeoutSeconds);

                return client;
            });

            services.AddTransient<PackageService>();
            services.AddTransient<IPackageDownloader, PackageDownloader>();

            // TODO: This is a bit of a hack! The IndexingService depends on IPackageService,
            // and RemotePackageService (which is an IPackageService) depends on IndexingService.
            // Prevent infinite recursion by forcing IndexingService to use PackageService.
            services.AddTransient<IIndexingService, IndexingService>(s =>
            {
                return new IndexingService(
                    s.GetRequiredService<PackageService>(),
                    s.GetRequiredService<IPackageStorageService>(),
                    s.GetRequiredService<ILogger<IndexingService>>());
            });

            services.AddTransient<IPackageService, RemotePackageService>(s =>
            {
                var options = s.GetRequiredService<IOptions<Options>>().Value;

                return new RemotePackageService(
                    options.PackageSource,
                    s.GetRequiredService<PackageService>(),
                    s.GetRequiredService<IPackageDownloader>(),
                    s.GetRequiredService<IIndexingService>(),
                    s.GetRequiredService<ILogger<RemotePackageService>>());
            });

            services.AddTransient<IPackageStorageService, FilePackageStorageService>(s =>
            {
                var options = s.GetRequiredService<IOptions<Options>>().Value;

                return new FilePackageStorageService(options.PackageStore);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseStatusCodePages();

                // Run migrations automatically in development mode.
                var scopeFactory = app.ApplicationServices
                    .GetRequiredService<IServiceScopeFactory>();

                using (var scope = scopeFactory.CreateScope())
                {
                    scope.ServiceProvider
                        .GetRequiredService<Context>()
                        .Database
                        .Migrate();
                }
            }

            app.UseMvc(routes =>
            {
                // Service index
                routes.MapRoute(IndexRouteName, "v3/index.json", defaults: new { controller = "Index", action = "Get" });

                // Package Publish
                routes.MapRoute(
                    UploadRouteName,
                    "v2/package",
                    defaults: new { controller = "PackagePublish", action = "Upload" },
                    constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

                routes.MapRoute(
                    DeleteRouteName,
                    "v2/package/{id}/{version}",
                    defaults: new { controller = "PackagePublish", action = "Delete" },
                    constraints: new { httpMethod = new HttpMethodRouteConstraint("DELETE") });

                routes.MapRoute(
                    RelistRouteName,
                    "v2/package/{id}/{version}",
                    defaults: new { controller = "PackagePublish", action = "Relist" },
                    constraints: new { httpMethod = new HttpMethodRouteConstraint("POST") });

                // Search
                routes.MapRoute(
                    SearchRouteName,
                    "v3/search",
                    defaults: new { controller = "Search", action = "Get" });

                routes.MapRoute(
                    AutocompleteRouteName,
                    "v3/autocomplete",
                    defaults: new { controller = "Search", action = "Autocomplete" });

                // Registration
                routes.MapRoute(
                    RegistrationIndexRouteName,
                    "v3/registration/{id}/index.json",
                    defaults: new { controller = "RegistrationIndex", action = "Get" });

                routes.MapRoute(
                    RegistrationLeafRouteName,
                    "v3/registration/{id}/{version}.json",
                    defaults: new { controller = "RegistrationLeaf", action = "Get" });

                // Package Content
                routes.MapRoute(
                    PackageVersionsRouteName,
                    "v3/package/{id}/index.json",
                    defaults: new { controller = "Package", action = "Versions" });

                routes.MapRoute(
                    PackageDownloadRouteName,
                    "v3/package/{id}/{version}/{idVersion}.nupkg",
                    defaults: new { controller = "Package", action = "DownloadPackage" });

                routes.MapRoute(
                    PackageDownloadManifestRouteName,
                    "v3/package/{id}/{version}/{id2}.nuspec",
                    defaults: new { controller = "Package", action = "DownloadNuspec" });
            });
        }
    }
}
