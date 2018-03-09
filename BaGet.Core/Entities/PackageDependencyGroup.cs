using System.Collections.Generic;

namespace BaGet.Core.Entities
{
    // See NuGetGallery.Core's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/PackageDependency.cs
    public class PackageDependencyGroup
    {
        public string TargetFramework { get; set; }

        public List<PackageDependency> Dependencies { get; set; }
    }
}
