using BaGet.Core.Entities;

namespace BaGet.Legacy
{
    public class PackageWithUrls
    {
        private readonly string resourceIdUrl;
        private readonly string packageContentUrl;
        private readonly ODataPackage pkg;

        public PackageWithUrls(ODataPackage pkg, string resourceIdUrl, string packageContentUrl)
        {
            this.pkg = pkg;
            this.resourceIdUrl = resourceIdUrl;
            this.packageContentUrl = packageContentUrl;
        }

        public PackageWithUrls(Package pkg, string resourceIdUrl, string packageContentUrl)
        {
            this.pkg = new ODataPackage(pkg);
            this.resourceIdUrl = resourceIdUrl;
            this.packageContentUrl = packageContentUrl;
        }

        public string ResourceIdUrl => resourceIdUrl;

        public string PackageContentUrl => packageContentUrl;

        public ODataPackage Pkg => pkg;
    }
}
