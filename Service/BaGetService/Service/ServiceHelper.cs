using System.Diagnostics;

namespace BaGet.Service.Service;

public static class ServiceHelper
{
    public static async Task UnRegister()
    {
        Process process;
        try
        {
            process = Process.Start("net.exe",
                $"stop BaGetService");
            await process.WaitForExitAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.Read();
        }

        try
        {
            process = Process.Start("sc.exe",
                $"delete BaGetService");
            await process.WaitForExitAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.Read();
        }

        Environment.Exit(0);
    }

    public static async Task Register()
    {
        try
        {
            var arguments =
                $"create \"BaGetService\" binpath=\"dotnet.exe \\\"{typeof(Program).Assembly.Location}\\\"\" start=auto";
            var process = Process.Start("sc.exe", arguments);
            await process.WaitForExitAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.Read();
        }

        try
        {
            var process = Process.Start("net.exe",
                $"start BaGetService");
            await process.WaitForExitAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.Read();
        }

        Environment.Exit(0);
    }
}
