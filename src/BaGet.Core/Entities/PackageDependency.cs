namespace BaGet.Core.Entities
{
    // See NuGetGallery.Core's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/PackageDependency.cs
    public class PackageDependency : EntityBase
    {
        public virtual string PackageId { get; set; }
        public virtual string VersionRange { get; set; }
        public virtual string TargetFramework { get; set; }
    }
}
