using System;
using System.Collections.Generic;
using NuGet.Versioning;

namespace BaGet.Core
{
    // See NuGetGallery's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/Package.cs
    public class Package
    {
        public int Key { get; set; }

        public string Id { get; set; }

        public NuGetVersion Version
        {
            get
            {
                // Favor the original version string as it contains more information.
                // Packages uploaded with older versions of BaGet may not have the original version string.
                return NuGetVersion.Parse(
                    OriginalVersionString != null
                        ? OriginalVersionString
                        : NormalizedVersionString);
            }

            set
            {
                NormalizedVersionString = value.ToNormalizedString().ToLowerInvariant();
                OriginalVersionString = value.OriginalVersion;
            }
        }

        public string[] Authors { get; set; }
        public string Description { get; set; }
        public long Downloads { get; set; }
        public bool HasReadme { get; set; }
        public bool HasEmbeddedIcon { get; set; }
        public bool IsPrerelease { get; set; }
        public string ReleaseNotes { get; set; }
        public string Language { get; set; }
        public bool Listed { get; set; }
        public string MinClientVersion { get; set; }
        public DateTime Published { get; set; }
        public bool RequireLicenseAcceptance { get; set; }
        public SemVerLevel SemVerLevel { get; set; }
        public string Summary { get; set; }
        public string Title { get; set; }

        public Uri IconUrl { get; set; }
        public Uri LicenseUrl { get; set; }
        public Uri ProjectUrl { get; set; }

        public Uri RepositoryUrl { get; set; }
        public string RepositoryType { get; set; }

        public string[] Tags { get; set; }

        /// <summary>
        /// Used for optimistic concurrency.
        /// </summary>
        public byte[] RowVersion { get; set; }

        public List<PackageDependency> Dependencies { get; set; }
        public List<PackageType> PackageTypes { get; set; }
        public List<TargetFramework> TargetFrameworks { get; set; }

        public string NormalizedVersionString { get; set; }
        public string OriginalVersionString { get; set; }


        public string IconUrlString => IconUrl?.AbsoluteUri ?? string.Empty;
        public string LicenseUrlString => LicenseUrl?.AbsoluteUri ?? string.Empty;
        public string ProjectUrlString => ProjectUrl?.AbsoluteUri ?? string.Empty;
        public string RepositoryUrlString => RepositoryUrl?.AbsoluteUri ?? string.Empty;
    }
}
