namespace BaGet.Core
{
    // See NuGetGallery.Core's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/PackageType.cs
    public class PackageType
    {
        public int Key { get; set; }

        public string Name { get; set; }
        public string Version { get; set; }

        public Package Package { get; set; }
    }
}
