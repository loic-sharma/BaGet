using System;
using System.Collections.Generic;
using NuGet.Versioning;

namespace BaGet.Core.Entities
{
    // See NuGetGallery's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/Package.cs
    public class Package
    {
        public int Key { get; set; }

        public string Id { get; set; }

        public string[] Authors { get; set; }
        public string Description { get; set; }
        public long Downloads { get; set; }
        public bool HasReadme { get; set; }
        public string Language { get; set; }
        public bool Listed { get; set; }
        public string MinClientVersion { get; set; }
        public DateTime Published { get; set; }
        public bool RequireLicenseAcceptance { get; set; }
        public string Summary { get; set; }
        public string Title { get; set; }

        public string IconUrl { get; set; }
        public string LicenseUrl { get; set; }
        public string ProjectUrl { get; set; }

        public string RepositoryUrl { get; set; }
        public string RepositoryType { get; set; }

        public string[] Tags { get; set; }

        /// <summary>
        /// Used for optimistic concurrency.
        /// </summary>
        public byte[] RowVersion { get; set; }

        public List<PackageDependency> Dependencies { get; set; }

        public string Version { get; set; }
        /*
        {
            get => Version?.ToNormalizedString().ToLowerInvariant() ?? string.Empty;
            set
            {
                NuGetVersion.TryParse(value, out var version);

                Version = version;
            }
        }*/
    }
}
