using System.IO;
using NuGet.Packaging;
using NuGet.Versioning;

namespace BaGet.Validation.Pipeline;

public sealed class PackageValidationContext
{
    public PackageValidationContext(string packageId, NuGetVersion packageVersion, Stream package, PackageArchiveReader reader)
    {
        PackageId = packageId;
        PackageVersion = packageVersion;
        Package = package;
        Reader = reader;
    }

    public string PackageId { get; }

    public NuGetVersion PackageVersion { get; }

    /// <summary>
    /// The raw package. Seekable; a validator must not dispose it.
    /// </summary>
    public Stream Package { get; }

    /// <summary>
    /// A reader over <see cref="Package"/>. A validator must not dispose it.
    /// </summary>
    public PackageArchiveReader Reader { get; }
}
