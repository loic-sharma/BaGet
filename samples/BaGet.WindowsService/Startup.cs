namespace BaGet.WindowsService
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddBaGetWebApplication(app =>
            {
                // Use SQLite as BaGet's database and store packages on the local file system.
                app.AddSqliteDatabase();
                //app.AddPostgreSqlDatabase();
                app.AddFileStorage();
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseStaticFiles();
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                // Add BaGet's endpoints.
                var baget = new BaGetEndpointBuilder();

                baget.MapEndpoints(endpoints);
            });
        }
    }
}
