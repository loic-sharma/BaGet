using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace BaGet.Core.Entities
{
    // See NuGetGallery's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/Package.cs
    public class Package
    {
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

        public List<PackageDependencyGroup> Dependencies { get; set; }

        [NotMapped]
        public string[] Tags
        {
            get => _tags == null ? new string[0] : JsonConvert.DeserializeObject<string[]>(_tags);
            set => _tags = JsonConvert.SerializeObject(value);
        }

        internal string _tags { get; set; }
    }
}
