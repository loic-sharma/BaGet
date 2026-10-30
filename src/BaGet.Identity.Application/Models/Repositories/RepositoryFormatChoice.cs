using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Repositories;

/// <summary>A package format a repository can be created for, with the extra fields its create form asks (the JSON form of an <see cref="IFormatModule"/>).</summary>
/// <param name="Format">The format.</param>
/// <param name="DisplayName">Its name for people (<c>npm</c>, <c>Maven</c>).</param>
/// <param name="CreateFields">What the administrator fills in to create a local repository of it, besides the name.</param>
public sealed record RepositoryFormatChoice(RepositoryFormat Format, string DisplayName, IReadOnlyList<FormatCreateField> CreateFields)
{
    /// <summary>The choice for <paramref name="module"/>.</summary>
    public static RepositoryFormatChoice Of(IFormatModule module)
        => new(module.Format, module.DisplayName, module.CreateFields);
}
