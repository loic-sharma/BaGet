using System;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// Flags to control the behavior of <see cref="NuGetVersionConverter"/>
    /// and <see cref="NuGetVersionListConverter"/>.
    /// </summary>
    [Flags]
    public enum NuGetVersionConversionFlags
    {
        /// <summary>
        /// Normalize, lowercase, and trim any SemVer 2.0.0 build metadata
        /// from the version string.
        /// </summary>
        Default = 0,

        /// <summary>
        /// Do not lowercase the version string.
        /// </summary>
        OriginalCasing = 1,

        /// <summary>
        /// Keep the SemVer 2.0.0 build metadata in the version string, if any.
        /// </summary>
        IncludeBuildMetadata = 2,
    }
}
