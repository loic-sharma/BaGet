﻿using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core.Services
{
    public interface IPackageDeletionService
    {
        /// <summary>
        /// Attempt to delete a package.
        /// </summary>
        /// <param name="id">The id of the package to delete.</param>
        /// <param name="version">The version of the package to delete.</param>
        /// <returns>False if the package does not exist.</returns>
        Task<bool> TryDeletePackageAsync(string id, NuGetVersion version);
    }
}
