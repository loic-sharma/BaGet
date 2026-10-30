using System;
using System.Collections.Generic;
using System.Linq;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>The "My packages" screen: the packages the user owns in each repository.</summary>
public sealed class AccountPackagesViewModel
{
    /// <summary>The owned packages of each repository that has any, <c>default</c> first.</summary>
    public IReadOnlyList<RepositoryPackages> Groups { get; set; } = Array.Empty<RepositoryPackages>();

    /// <summary>Whether the user owns nothing in any repository.</summary>
    public bool IsEmpty => Groups.Count == 0;

    /// <summary>Whether the list needs repository headings: more than the default repository holds packages of the user.</summary>
    public bool ShowRepositoryNames => Groups.Any(g => !g.Repository.IsDefault);
}
