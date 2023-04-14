using System.Diagnostics;

namespace BaGet.Service.NuGet
{
    public static class NuGetHelper
    {

        public static async Task Push(string directoryName)
        {
            try
            {
                if (!Directory.Exists(directoryName))
                {
                    Console.WriteLine($"The directory '{directoryName}' does not exists.");
                    Environment.Exit(-1);
                }

                var builder = new ConfigurationBuilder();
                builder
                    .SetBasePath(Path.GetDirectoryName(typeof(Program).Assembly.Location)!)
                    .AddJsonFile("appsettings.json", false);

                var configuration = builder.Build();
                var section = configuration.GetSection("ApiKey");

                var apikey = section.Value;

                foreach (var file in Directory.GetFiles(directoryName, "*.*pkg"))
                {
                    try
                    {
                        var dotnetExe = "dotnet.exe";
                        var arguments = $"nuget push -s http://localhost:6060/v3/index.json --api-key {apikey} {file}";
                        Console.WriteLine($"Running:\n]n{dotnetExe} {arguments}\n\n");
                        var process = Process.Start(dotnetExe,
                            arguments);
                        await process.WaitForExitAsync();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        Console.Read();
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.Read();
            }

            Environment.Exit(0);
        }
    }
}
