using System.IO;
using System.Threading.Tasks;
using BaGet;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using BaGet.Web.V3.Extensions;

namespace BaGetWebApplication;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddBaGetWebApplication(app =>
        {
            // Use SQLite as BaGet's database and store packages on the local file system.
            app.AddSqliteDatabase();
            app.AddFileStorage();
        });

        // The web UI (screens and views); leave it out for a host that serves only the APIs.
        builder.Services.AddBaGetFrontEnd();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseStaticFiles();

        // The layout links the UI's static files at /_content/BaGet.Web/...; the BaGet host serves its wwwroot there. This sample
        // serves the same folder from the repository (a real embedding host ships a copy of it: see the Extension.BaGet package).
        var uiFiles = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "src", "BaGet", "wwwroot"));
        if (Directory.Exists(uiFiles))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(uiFiles),
                RequestPath = "/_content/BaGet.Web",
            });
        }

        app.UseRouting();

        // Required: selects the repository of /r/{repo}/ requests (404 for an unknown one). Without it those routes would be
        // served from the default repository, so BaGet answers them 500. Keep it before authentication, if you add any.
        app.UseRepositoryResolution();

        // Writes answer 503 while the server is read-only (a database copy, for one), and a cancelled request is not an error.
        app.UseOperationCancelledMiddleware();
        app.UseMaintenanceMiddleware();

        // This sample adds no authentication, so every caller is anonymous: see BaGet.Web.V3's README to add accounts or an API key.

        // Add BaGet's endpoints.
        new BaGetEndpointBuilder().MapEndpoints(app);

        await app.RunMigrationsAsync();
        await app.RunAsync();
    }
}
