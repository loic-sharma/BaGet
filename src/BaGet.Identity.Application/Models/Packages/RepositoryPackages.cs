using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>
/// The packages the user owns in one repository.
/// </summary>
public sealed record RepositoryPackages(RepositoryInfo Repository, IReadOnlyList<OwnedPackage> Packages);
