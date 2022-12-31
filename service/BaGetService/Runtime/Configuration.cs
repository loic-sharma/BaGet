namespace BaGet.Service.Runtime
{
    public static class Configuration
    {
        public static string GetDirectoryName(string[] args)
        {
            var directoryName = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "PackagesToPublish");

            var lastOrDefault = (args.LastOrDefault() ?? "").Trim('"');
            if (!string.IsNullOrEmpty(lastOrDefault) && Directory.Exists(lastOrDefault))
            {
                directoryName = lastOrDefault;
            }

            Console.WriteLine($"Please insert the directory containing packages ('{directoryName}' if empty)");
            var readLine = Console.ReadLine();
            if (!string.IsNullOrEmpty(readLine) && Directory.Exists(readLine))
            {
                directoryName = readLine;
            }

            return directoryName;
        }
    }
}
