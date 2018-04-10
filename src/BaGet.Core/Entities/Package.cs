using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Core.Entities
{
    // See NuGetGallery's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/Package.cs
    public class Package
    {
        public int Key { get; set; }

        public string Id { get; set; }
        public NuGetVersion Version { get; set; }

        // TODO: This should be string[]
        public string Authors { get; set; }
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

        public Uri IconUrl { get; set; }
        public Uri LicenseUrl { get; set; }
        public Uri ProjectUrl { get; set; }

        public string[] Tags { get; set; }

        public List<PackageDependencyGroup> Dependencies { get; set; }

        public string VersionString
        {
            get => Version?.ToNormalizedString() ?? string.Empty;
            set
            {
                NuGetVersion.TryParse(value, out var version);

                Version = version;
            }
        }

        public string IconUrlString
        {
            get => IconUrl?.AbsoluteUri ?? string.Empty;
            set => IconUrl = (!string.IsNullOrEmpty(value)) ? new Uri(value) : null;
        }

        public string LicenseUrlString
        {
            get => LicenseUrl?.AbsoluteUri ?? string.Empty;
            set => LicenseUrl = (!string.IsNullOrEmpty(value)) ? new Uri(value) : null;
        }

        public string ProjectUrlString
        {
            get => ProjectUrl?.AbsoluteUri ?? string.Empty;
            set => ProjectUrl = (!string.IsNullOrEmpty(value)) ? new Uri(value) : null;
        }

        public string TagsString
        {
            get => JsonConvert.SerializeObject(Tags);
            set => Tags = (!string.IsNullOrEmpty(value)) ? JsonConvert.DeserializeObject<string[]>(value) : new string[0];
        }
    }
}
