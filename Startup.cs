using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
            }

            app.UseMvc(routes =>
            {
                routes.MapRoute("index", "v3/index.json", defaults: new { controller = "Index", action = "Get"});

                routes.MapRoute(
                    "search",
                    "v3/search",
                    defaults: new { controller = "Search", action = "Get"});

                routes.MapRoute(
                    "registration-index",
                    "v3/registration/{id}.json",
                    defaults: new { controller = "RegistrationIndex", action = "Get"});

                routes.MapRoute(
                    "registration-leaf",
                    "v3/registration/{id}/{version}.json",
                    defaults: new { controller = "RegistrationLeaf", action = "Get"});
            });
        }
    }
}
