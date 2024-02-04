using BaGet.Web;

namespace BaGet.WindowsService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args);
            builder.ConfigureAppConfiguration((ctx, config) =>
            {
                var root = Environment.GetEnvironmentVariable("BAGET_CONFIG_ROOT");

                if (!string.IsNullOrEmpty(root))
                {
                    config.SetBasePath(root);
                }
            });
            builder.ConfigureWebHostDefaults(web =>
            {
                web.ConfigureKestrel(options =>
                {
                    // Remove the upload limit from Kestrel. If needed, an upload limit can
                    // be enforced by a reverse proxy server, like IIS.
                    options.Limits.MaxRequestBodySize = null;
                });

                web.UseStartup<Startup>();
            });
            builder.UseWindowsService();

            var host = builder.Build();
            if (!host.ValidateStartupOptions())
            {
                return;
            }

            host.RunMigrationsAsync().Wait();
            host.RunAsync().Wait();
        }
    }
}
