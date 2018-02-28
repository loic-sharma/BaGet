using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseStatusCodePages();
            }

            app.UseMvc(routes =>
            {
                // Service index
                routes.MapRoute("index", "v3/index.json", defaults: new { controller = "Index", action = "Get" });

                // Package Publish
                routes.MapRoute(
                    "upload",
                    "v2/package",
                    defaults: new { controller = "PackagePublish", action = "Upload" },
                    constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

                routes.MapRoute(
                    "delete",
                    "v2/package/{id}/{version}",
                    defaults: new { controller = "PackagePublish", action = "Delete" },
                    constraints: new { httpMethod = new HttpMethodRouteConstraint("DELETE") });

                routes.MapRoute(
                    "relist",
                    "v2/package/{id}/{version}",
                    defaults: new { controller = "PackagePublish", action = "Relist" },
                    constraints: new { httpMethod = new HttpMethodRouteConstraint("POST") });

                // Search
                routes.MapRoute(
                    "search",
                    "v3/search",
                    defaults: new { controller = "Search", action = "Get" });

                // Registration
                routes.MapRoute(
                    "registration-index",
                    "v3/registration/{id}.json",
                    defaults: new { controller = "RegistrationIndex", action = "Get" });

                routes.MapRoute(
                    "registration-leaf",
                    "v3/registration/{id}/{version}.json",
                    defaults: new { controller = "RegistrationLeaf", action = "Get" });

                // Package Content
                routes.MapRoute(
                    "package-versions",
                    "v3/package/{id}/index.json",
                    defaults: new { controller = "Package", action = "Versions" });

                routes.MapRoute(
                    "package-download",
                    "v3/package/{id}/{version}/{idVersion}.nupkg",
                    defaults: new { controller = "Package", action = "DownloadPackage" });

                routes.MapRoute(
                    "package-download-manifest",
                    "v3/package/{id}/{version}/{id2}.nuspec",
                    defaults: new { controller = "Package", action = "DownloadNuspec" });
            });
        }
    }
}
