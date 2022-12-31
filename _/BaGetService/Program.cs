using BaGet.Service.NuGet;
using BaGet.Service.Runtime;
using BaGet.Service.Service;

namespace BaGet.Service;

public static class Program
{
    public static string[] Arguments = Array.Empty<string>();

    public static async Task Main(string[] args)
    {
        Arguments = args;
        if (Arguments.Any(a => a.ToLower() == "register"))
        {
            await ServiceHelper.Register();
        }

        if (args.Any(a => a.ToLower() == "package"))
        {
            var directoryName = Configuration.GetDirectoryName(args);

            await NuGetHelper.Push(directoryName);
        }

        if (args.Any(a => a.ToLower() == "delete"))
        {
            await ServiceHelper.UnRegister();
        }

        await BaGetRunner.RunBaGet(args);
    }
}
