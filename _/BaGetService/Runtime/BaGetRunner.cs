using BaGet.Service.Service;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using Serilog;

namespace BaGet.Service.Runtime
{
    public static class BaGetRunner
    {
        public static async Task RunBaGet(string[] args)
        {
            Environment.CurrentDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
            using var host = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options => { options.ServiceName = "BaGetService"; })
                .ConfigureServices(services =>
                {
                    LoggerProviderOptions.RegisterProviderOptions<
                        EventLogSettings, EventLogLoggerProvider>(services);

                    services.AddHostedService<BaGetService>();
                })
                .ConfigureLogging(
                    loggingBuilder =>
                    {
                        var configuration = new ConfigurationBuilder()
                            .AddJsonFile("appsettings.json")
                            .Build();
                        var logger = new LoggerConfiguration()
                            .ReadFrom.Configuration(configuration)
                            .CreateLogger();
                        loggingBuilder.AddSerilog(logger, true);
                    }).Build();
            await host.RunAsync();
        }
    }
}
