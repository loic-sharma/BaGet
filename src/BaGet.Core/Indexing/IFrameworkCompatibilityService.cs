using System.Collections.Generic;

namespace BaGet.Core
{
    /// <summary>
    /// Used to determine the compatibility matrix between frameworks.
    /// </summary>
    public interface IFrameworkCompatibilityService
    {
        /// <summary>
        /// Given a framework, find all other compatible frameworks.
        /// </summary>
        /// <param name="framework">The input framework.</param>
        /// <returns>The list of compatible frameworks.</returns>
        IReadOnlyList<string> FindAllCompatibleFrameworks(string framework);
    }
}
