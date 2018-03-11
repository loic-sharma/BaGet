using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace BaGet.Core.Entities
{
    // See NuGetGallery's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/Package.cs
    public class Package
    {
        public int Key { get; set; }

        public string Id { get; set; }
        public string Version { get; set; }

        public string Authors { get; set; }
        public string Description { get; set; }
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

        public string IconUrlString
        {
            get => IconUrl?.AbsoluteUri;
            set => IconUrl = (value != null) ? new Uri(value) : null;
        }

        public string LicenseUrlString
        {
            get => LicenseUrl?.AbsoluteUri;
            set => LicenseUrl = (value != null) ? new Uri(value) : null;
        }

        public string ProjectUrlString
        {
            get => ProjectUrl?.AbsoluteUri;
            set => ProjectUrl = (value != null) ? new Uri(value) : null;
        }

        public string TagsString
        {
            get => JsonConvert.SerializeObject(Tags);
            set => Tags = (value != null) ? JsonConvert.DeserializeObject<string[]>(value) : new string[0];
        }
    }
}
