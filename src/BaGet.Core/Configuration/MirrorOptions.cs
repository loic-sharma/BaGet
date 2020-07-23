using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BaGet.Core
{
    public class MirrorOptions : IValidatableObject
    {
        /// <summary>
        /// If true, packages that aren't found locally will be indexed
        /// using the upstream source.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// The v3 index that will be mirrored.
        /// </summary>
        public Uri PackageSource { get; set; }

        /// <summary>
        /// The time before a download from the package source times out.
        /// </summary>
        [Range(0, int.MaxValue)]
        public int PackageDownloadTimeoutSeconds { get; set; } = 600;
        /// <summary>
        /// The ```Exclude``` list allows to specify regular expressions to prevent indexing certain packages from upstream source, e.g. to prevent clashes with internal packages.
        /// </summary>
        public IEnumerable<string> Exclude { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Enabled && PackageSource == null)
            {
                yield return new ValidationResult(
                    $"The {nameof(PackageSource)} configuration is required if mirroring is enabled",
                    new[] { nameof(PackageSource) });
            }
        }
    }
}
