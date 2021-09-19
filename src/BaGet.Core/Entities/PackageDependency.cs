namespace BaGet.Core
{
    // See NuGetGallery.Core's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/PackageDependency.cs
    public class PackageDependency
    {
        public int Key { get; set; }

        /// <summary>
        /// The dependency's package ID. Null if this is a dependency group without any dependencies.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The dependency's package version. Null if this is a dependency group without any dependencies.
        /// </summary>
        public string VersionRange { get; set; }

        public string TargetFramework { get; set; }

        public Package Package { get; set; }
    }
}
