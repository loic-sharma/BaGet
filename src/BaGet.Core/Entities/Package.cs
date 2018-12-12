using System;
using System.Collections.Generic;

namespace BaGet.Core.Entities
{
    // See NuGetGallery's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/Package.cs
    public class Package : EntityBase
    {
        public virtual string PackageId { get; set; }

        public virtual string[] Authors { get; set; }
        public virtual string Description { get; set; }
        public virtual long Downloads { get; set; }
        public virtual bool HasReadme { get; set; }
        public virtual string Language { get; set; }
        public virtual bool Listed { get; set; }
        public virtual string MinClientVersion { get; set; }
        public virtual DateTime Published { get; set; }
        public virtual bool RequireLicenseAcceptance { get; set; }
        public virtual string Summary { get; set; }
        public virtual string Title { get; set; }

        public virtual string IconUrl { get; set; }
        public virtual string LicenseUrl { get; set; }
        public virtual string ProjectUrl { get; set; }

        public virtual string RepositoryUrl { get; set; }
        public virtual string RepositoryType { get; set; }

        public virtual IList<PackageTag> Tags { get; set; }
        public virtual IList<PackageDependency> Dependencies { get; set; }

        public virtual string Version { get; set; }

        public Package()
        {
            Dependencies = new List<PackageDependency>();
            Tags = new List<PackageTag>();
        }
    }
}
