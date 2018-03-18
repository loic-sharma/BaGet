namespace BaGet.Core.Entities
{
    // See NuGetGallery.Core's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/PackageDependency.cs
    public class PackageDependency
    {
        public int Key { get; set; }

        public string Id { get; set; }
        public string VersionRange { get; set; }
    }
}
