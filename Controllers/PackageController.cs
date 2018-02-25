using System;

namespace BaGet.Controllers
{
    public class PackageController
    {
        public string[] Versions(string id)
        {
            return new[] { "1.0.0" };
        }

        public object DownloadPackage(string id, string version, string idVersion)
        {
            if (idVersion != $"{id}.{version}")
            {
                throw new Exception("Todo - 404");
            }

            // TODO: Will redirect work??
            return "Download package";
        }

        public object DownloadNuspec(string id, string version)
        {
            return "Download nuspec";
        }
    }
}