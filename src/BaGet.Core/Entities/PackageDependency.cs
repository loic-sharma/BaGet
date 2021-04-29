namespace BaGet.Core
{
    // See NuGetGallery.Core's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/PackageDependency.cs
    public class PackageDependency
    {
        public int Key { get; set; }

        public string Id { get; set; }
        public string VersionRange { get; set; }
        public string TargetFramework { get; set; }

        public int? PackageKey { get; set; }
        public Package Package { get; set; }
    }
}
