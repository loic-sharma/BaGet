using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;

namespace BaGet.Core
{
    public class FileSystemStorageOptions : IValidatableObject
    {
        /// <summary>
        /// The path at which content will be stored. Defaults to the same path
        /// as the main BaGet executable. This path will be created if it does not
        /// exist at startup. Packages will be stored in a subfolder named "packages".
        /// </summary>
        public string Path { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Convert an empty storage path to the current working directory.
            if (string.IsNullOrEmpty(Path))
            {
                Path = Directory.GetCurrentDirectory();
            }

            return Enumerable.Empty<ValidationResult>();
        }
    }
}
